using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Util;

namespace Squared.Illuminant {
    public class ParticleSystem : IDisposable {
        internal struct SpawnState {
            public int Offset, Free;
        }

        internal class LivenessInfo : IDisposable {
            public int            ID;
            public int?           Count;
            public OcclusionQuery Query;
            public long           LastQueryStart;
            public bool           IsQueryPending;
            public int            DeadFrameCount;
            public bool           SpawnedParticlesThisFrame;

            public void Dispose () {
                Query.Dispose();
                Query = null;
            }
        }

        internal class Slice : IDisposable, IEnumerable<Slice.Chunk> {
            public class Chunk : IDisposable {
                public const int Width = 256;
                public const int Height = 256;
                public const int MaximumCount = Width * Height;

                public int ID;
                public int RefCount;

                public RenderTargetBinding[] Bindings;

                public RenderTarget2D PositionAndLife;
                public RenderTarget2D Velocity;
                public RenderTarget2D Attributes;

                public Chunk (
                    int id, int attributeCount, GraphicsDevice device
                ) {
                    ID = id;

                    Bindings = new RenderTargetBinding[2 + attributeCount];
                    Bindings[0] = PositionAndLife = CreateRenderTarget(device);
                    Bindings[1] = Velocity = CreateRenderTarget(device);

                    if (attributeCount == 1)
                        Bindings[2] = Attributes = CreateRenderTarget(device);
                }

                private RenderTarget2D CreateRenderTarget (GraphicsDevice device) {
                    return new RenderTarget2D(
                        device, 
                        Width, Height, false, 
                        SurfaceFormat.Vector4, DepthFormat.None, 
                        0, RenderTargetUsage.PreserveContents
                    );
                }

                // Make sure to lock the slice first.
                public void Initialize<TAttribute> (
                    int offset,
                    Action<Vector4[], int> positionInitializer,
                    Action<Vector4[], int> velocityInitializer,
                    Action<TAttribute[], int> attributeInitializer
                ) where TAttribute : struct {
                    var buf = new Vector4[MaximumCount];

                    if (positionInitializer != null) {
                        positionInitializer(buf, offset);
                        PositionAndLife.SetData(buf);
                    }

                    if (velocityInitializer != null) {
                        velocityInitializer(buf, offset);
                        Velocity.SetData(buf);
                    }

                    if ((attributeInitializer != null) && (Attributes != null)) {
                        TAttribute[] abuf;
                        if (typeof(TAttribute) == typeof(Vector4))
                            abuf = buf as TAttribute[];
                        else
                            abuf = new TAttribute[MaximumCount];

                        attributeInitializer(abuf, offset);
                        Attributes.SetData(abuf);
                    }
                }

                public void Dispose () {
                    PositionAndLife.Dispose();
                    Velocity.Dispose();
                    if (Attributes != null)
                        Attributes.Dispose();

                    PositionAndLife = Velocity = Attributes = null;
                }
            }

            public readonly int Index;
            public readonly int AttributeCount;
            public long Timestamp;
            public bool IsValid, IsBeingGenerated;
            public int  InUseCount;

            private readonly Dictionary<int, Chunk> Chunks = new Dictionary<int, Chunk>();

            public Slice (
                GraphicsDevice device, int index, int attributeCount
            ) {
                Index = index;
                AttributeCount = attributeCount;
                if ((attributeCount > 1) || (attributeCount < 0))
                    throw new ArgumentException("Valid attribute counts are 0 and 1");
                Timestamp = Squared.Util.Time.Ticks;
            }

            public void Lock (string reason) {
                // Console.WriteLine("Lock {0} for {1}", Index, reason);
                lock (this)
                    InUseCount++;
            }

            public void Unlock () {
                // Console.WriteLine("Unlock {0}", Index);
                lock (this)
                    InUseCount--;
            }

            public void Dispose () {
                IsValid = false;

                foreach (var kvp in Chunks)
                    kvp.Value.Dispose();
            }            

            public Chunk RemoveByID (int id) {
                Chunk result;
                if (Chunks.TryGetValue(id, out result)) {
                    Chunks.Remove(id);
                    result.RefCount--;
                }
                return result;
            }

            public void Add (Chunk chunk) {
                Chunks.Add(chunk.ID, chunk);
                chunk.RefCount++;
            }

            public Chunk GetByID (int id) {
                Chunk result;
                Chunks.TryGetValue(id, out result);
                return result;
            }

            public Dictionary<int, Chunk>.ValueCollection.Enumerator GetEnumerator () {
                return Chunks.Values.GetEnumerator();
            }

            IEnumerator<Chunk> IEnumerable<Chunk>.GetEnumerator () {
                return Chunks.Values.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator () {
                return Chunks.Values.GetEnumerator();
            }

            public int Count {
                get {
                    return Chunks.Count;
                }
            }
        }

        public          int LiveCount { get; private set; }

        public readonly ParticleEngine                     Engine;
        public readonly ParticleSystemConfiguration        Configuration;
        public readonly List<Transforms.ParticleTransform> Transforms = 
            new List<Illuminant.Transforms.ParticleTransform>();

        // 3 because we go
        // old -> a -> b -> a -> ... -> done
        private const int SliceCount          = 3;
        private Slice[] Slices;

        private const int FreeListCapacity = 12;

        private readonly List<Slice.Chunk> NewChunks = new List<Slice.Chunk>();
        private readonly List<Slice.Chunk> FreeList = new List<Slice.Chunk>();

        private readonly Dictionary<int, LivenessInfo> LivenessInfos = new Dictionary<int, LivenessInfo>();
        private readonly Dictionary<int, SpawnState> SpawnStates = new Dictionary<int, SpawnState>();

        private readonly AutoResetEvent UnlockedEvent = new AutoResetEvent(true);

        internal long LastClearTimestamp;

        private int LastResetCount = 0;
        public event Action<ParticleSystem> OnDeviceReset;

        public ParticleSystem (
            ParticleEngine engine, ParticleSystemConfiguration configuration
        ) {
            Engine = engine;
            Configuration = configuration;
            LiveCount = 0;

            lock (engine.Coordinator.CreateResourceLock) {
                Slices = AllocateSlices();

                Slices[0].Timestamp = Time.Ticks;
                Slices[0].IsValid = true;
            }
        }

        public int Capacity {
            get {
                // FIXME
                return Slices[0].Count * Slice.Chunk.Width * Slice.Chunk.Height;
            }
        }

        private LivenessInfo GetLivenessInfo (int id) {
            LivenessInfo result;
            if (LivenessInfos.TryGetValue(id, out result))
                return result;

            OcclusionQuery query;
            lock (Engine.Coordinator.CreateResourceLock)
                query = new OcclusionQuery(Engine.Coordinator.Device);

            LivenessInfos.Add(
                id, result = new LivenessInfo {
                    ID = id,
                    Query = query,
                    Count = 0
                }
            );
            return result;
        }

        private Slice[] AllocateSlices () {
            var result = new Slice[SliceCount];
            for (var i = 0; i < result.Length; i++)
                result[i] = new Slice(Engine.Coordinator.Device, i, Configuration.AttributeCount);

            return result;
        }

        private Slice GrabWriteSlice () {
            Slice dest = null;

            lock (Slices) {
                for (int i = 0; i < 10; i++) {
                    dest = (
                        from s in Slices where (!s.IsBeingGenerated && s.InUseCount <= 0)
                        orderby s.Timestamp select s
                    ).FirstOrDefault();

                    if (dest == null) {
                        // Console.WriteLine("Retry lock");
                        UnlockedEvent.WaitOne(2);
                    } else
                        break;
                }

                if (dest == null)
                    throw new Exception("Failed to lock any slices for write");

                dest.Lock("write");
                lock (dest) {
                    dest.IsValid = false;
                    dest.IsBeingGenerated = true;
                    dest.Timestamp = Time.Ticks;
                }
            }

            return dest;
        }

        private volatile int NextChunkId = 1;
        
        private Slice.Chunk CreateChunk (GraphicsDevice device, int id) {
            lock (FreeList) {
                if (FreeList.Count > 0) {
                    var result = FreeList[0];
                    FreeList.RemoveAt(0);
                    result.ID = id;
                    return result;
                }
            }

            lock (Engine.Coordinator.CreateResourceLock)
                return new Slice.Chunk(id, Configuration.AttributeCount, device);
        }

        // Make sure to lock the slice first.
        public int InitializeNewChunks<TAttribute> (
            int particleCount,
            GraphicsDevice device,
            bool parallel,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            Action<TAttribute[], int> attributeInitializer
        ) where TAttribute : struct {
            var mc = Slice.Chunk.MaximumCount;
            int numToSpawn = (int)Math.Ceiling((double)particleCount / mc);

            if (parallel) {
                Parallel.For(
                    0, numToSpawn,
                    (i) => {
                        var c = CreateChunk(device, Interlocked.Increment(ref NextChunkId));
                        c.Initialize(i * mc, positionInitializer, velocityInitializer, attributeInitializer);
                        lock (NewChunks)
                            NewChunks.Add(c);
                    }
                );
            } else {
                for (int i = 0; i < numToSpawn; i++) {
                    var c = CreateChunk(device, Interlocked.Increment(ref NextChunkId));
                    c.Initialize(i * mc, positionInitializer, velocityInitializer, attributeInitializer);
                    lock (NewChunks)
                        NewChunks.Add(c);
                }
            }

            return numToSpawn * mc;
        }

        internal static Vector2 ChunkSize {
            get {
                return new Vector2(Slice.Chunk.Width, Slice.Chunk.Height);
            }
        }

        private int? GetSpawnTarget (int count) {
            foreach (var kvp in SpawnStates)
                if (kvp.Value.Free > count)
                    return kvp.Key;

            return null;
        }

        private void UpdatePass (
            IBatchContainer container, int layer, Material m,
            Slice source, Slice a, Slice b,
            ref Slice passSource, ref Slice passDest, 
            long startedWhen, Transforms.Spawner spawner,
            Action<EffectParameterCollection> setParameters
        ) {
            var _source = passSource;
            var _dest = passDest;
            var device = container.RenderManager.DeviceManager.Device;

            foreach (var chunk in _source) {
                var destChunk = _dest.GetByID(chunk.ID);
                if (destChunk == null) {
                    lock (container.RenderManager.CreateResourceLock)
                        destChunk = CreateChunk(device, chunk.ID);

                    _dest.Add(destChunk);
                }
            }

            int? spawnId = null;
            int spawnCount = 0;

            if (spawner != null) {
                spawner.Tick(out spawnCount);

                if (spawnCount <= 0)
                    return;
                else if (spawnCount > Slice.Chunk.MaximumCount)
                    throw new Exception("Spawn count too high to fit in a chunk");

                // FIXME: Inefficient. Spawn across two buffers?
                spawnId = GetSpawnTarget(spawnCount);
                if (spawnId == null) {
                    spawnId = GetSpawnTarget((int)spawner.MinCount);

                    if (spawnId.HasValue)
                        spawnCount = Math.Min(SpawnStates[spawnId.Value].Free, spawnCount);
                }

                if (spawnId == null) {
                    spawnId = Interlocked.Increment(ref NextChunkId);
                    SpawnStates[spawnId.Value] = new SpawnState { Offset = 0, Free = Slice.Chunk.MaximumCount };
                    lock (container.RenderManager.CreateResourceLock) {
                        _source.Add(CreateChunk(device, spawnId.Value));
                        _dest.Add(CreateChunk(device, spawnId.Value));
                    }
                }
            }

            var e = m.Effect;
            var p = e.Parameters;

            using (var batch = BatchGroup.New(
                container, layer,
                after: (dm, _) => {
                    // Incredibly pointless cleanup mandated by XNA's bugs
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                int i = 0;
                foreach (var sourceChunk in _source) {
                    var destChunk = _dest.GetByID(sourceChunk.ID);

                    var li = GetLivenessInfo(sourceChunk.ID);
                    UpdateChunkLivenessQuery(li);
                    var runQuery = !li.IsQueryPending;

                    if (runQuery) {
                        li.LastQueryStart = Time.Ticks;
                        li.IsQueryPending = true;
                    }

                    var chunkMaterial = m;
                    if (spawner != null) {
                        if (sourceChunk.ID != spawnId) {
                            chunkMaterial = Engine.ParticleMaterials.NullTransform;
                        } else {
                            chunkMaterial = m;

                            SpawnState spawnState;
                            if (!SpawnStates.TryGetValue(spawnId.Value, out spawnState))
                                spawnState = new SpawnState { Offset = Slice.Chunk.MaximumCount, Free = 0 };

                            spawner.SetIndices(spawnState.Offset, spawnState.Offset + spawnCount);

                            spawnState.Offset += spawnCount;
                            spawnState.Free -= spawnCount;

                            SpawnStates[spawnId.Value] = spawnState;

                            li.DeadFrameCount = 0;
                        }
                    }

                    ChunkUpdatePass(
                        batch, i++,
                        chunkMaterial, sourceChunk, destChunk,
                        setParameters, 
                        runQuery ? li.Query : null
                    );
                }
            }

            if (_source == source) {
                if (_dest == a)
                    passDest = b;
                else if (_dest == b)
                    passDest = a;
                else
                    throw new Exception();

                passSource = _dest;
            } else {
                passDest = _source;
                passSource = _dest;
            }
        }

        private void ChunkUpdatePass (
            IBatchContainer container, int layer, Material m,
            Slice.Chunk source, Slice.Chunk dest,
            Action<EffectParameterCollection> setParameters,
            OcclusionQuery query
        ) {
            // Console.WriteLine("{0} -> {1}", passSource.Index, passDest.Index);
            var e = m.Effect;
            var p = e.Parameters;
            using (var batch = NativeBatch.New(
                container, layer, m,
                (dm, _) => {
                    dm.Device.SetRenderTargets(dest.Bindings);
                    dm.Device.Viewport = new Viewport(0, 0, Slice.Chunk.Width, Slice.Chunk.Height);

                    if (query != null)
                        // For some reason this is a measurable performance hit
                        dm.Device.Clear(Color.Transparent);

                    p["Texel"].SetValue(new Vector2(1f / Slice.Chunk.Width, 1f / Slice.Chunk.Height));

                    if (setParameters != null)
                        setParameters(p);

                    if (source != null) {
                        p["PositionTexture"].SetValue(source.PositionAndLife);
                        p["VelocityTexture"].SetValue(source.Velocity);

                        var at = p["AttributeTexture"];
                        if (at != null)
                            at.SetValue(source.Attributes);
                    }

                    var dft = p["DistanceFieldTexture"];
                    if (dft != null)
                        dft.SetValue(Configuration.DistanceField.Texture);

                    var rt = p["RandomnessTexture"];
                    if (rt != null) {
                        p["RandomnessTexel"].SetValue(new Vector2(1.0f / ParticleEngine.RandomnessTextureWidth, 1.0f / ParticleEngine.RandomnessTextureHeight));
                        rt.SetValue(Engine.RandomnessTexture);
                    }

                    m.Flush();

                    if (query != null) {
                        var temp = query.IsComplete;

                        Monitor.Enter(query);
                        query.Begin();
                    }
                },
                (dm, _) => {
                    // XNA effectparameter gets confused about whether a value is set or not, so we do this
                    //  to ensure it always re-sets the texture parameter
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);

                    var at = p["AttributeTexture"];
                    if (at != null)
                        at.SetValue((Texture2D)null);

                    var dft = p["DistanceFieldTexture"];
                    if (dft != null)
                        dft.SetValue((Texture2D)null);

                    if (query != null) {
                        query.End();
                        Monitor.Exit(query);
                    }
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, Engine.QuadVertexBuffer, 0,
                    Engine.QuadIndexBuffer, 0, 0, Engine.QuadVertexBuffer.VertexCount, 0, Engine.QuadVertexBuffer.VertexCount / 2
                ));
            }
        }

        public void Clear () {
            // FIXME: Reap everything
            LastClearTimestamp = Time.Ticks;
        }

        public int Spawn (
            int particleCount,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            bool parallel = true
        ) {
            return Spawn<float>(particleCount, positionInitializer, velocityInitializer, null, parallel);
        }

        public int Spawn<TAttribute> (
            int particleCount,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            Action<TAttribute[], int> attributeInitializer,
            bool parallel = true
        ) where TAttribute : struct {
            var result = InitializeNewChunks(
                particleCount,
                Engine.Coordinator.Device,
                parallel,
                positionInitializer,
                velocityInitializer,
                attributeInitializer
            );
            return result;
        }

        internal HashSet<LivenessInfo> ChunksToReap = new HashSet<LivenessInfo>();

        private void UpdateChunkLivenessQuery (LivenessInfo target) {
            if (!target.IsQueryPending)
                return;
            if (target.Query.IsDisposed || !target.Query.IsComplete)
                return;

            target.IsQueryPending = false;

            if (target.LastQueryStart <= LastClearTimestamp) {
                target.Count = null;
                target.DeadFrameCount = 0;
            } else {
                target.Count = target.Query.PixelCount;

                if (target.Count > 0)
                    target.DeadFrameCount = 0;
            }
        }

        private void UpdateLivenessAndReapDeadChunks () {
            LiveCount = 0;

            foreach (var kvp in LivenessInfos) {
                var li = kvp.Value;
                UpdateChunkLivenessQuery(li);
                LiveCount += li.Count.GetValueOrDefault(0);

                if (li.Count.GetValueOrDefault(1) <= 0) {
                    li.DeadFrameCount++;
                    if (li.DeadFrameCount >= 4)
                        ChunksToReap.Add(li);
                }
            }

            foreach (var li in ChunksToReap) {
                lock (Slices)
                foreach (var s in Slices) {
                    var chunk = s.RemoveByID(li.ID);
                    if (chunk != null)
                        Reap(chunk);
                }

                SpawnStates.Remove(li.ID);
                LivenessInfos.Remove(li.ID);
            }

            ChunksToReap.Clear();
        }

        private void Reap (Slice.Chunk chunk) {
            lock (FreeList) {
                if (FreeList.Count < FreeListCapacity)
                    FreeList.Add(chunk);
                else
                    Engine.Coordinator.DisposeResource(chunk);
            }
        }

        public void Update (IBatchContainer container, int layer) {
            var startedWhen = Time.Ticks;

            Slice source, a, b;
            Slice passSource, passDest;

            if (LastResetCount != Engine.ResetCount) {
                if (OnDeviceReset != null)
                    OnDeviceReset(this);
                LastResetCount = Engine.ResetCount;
            }

            lock (LivenessInfos)
                UpdateLivenessAndReapDeadChunks();

            lock (Slices) {
                source = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).FirstOrDefault();

                if (source == null) {
                    // A clear occurred
                    return;
                }

                source.Lock("update");
            }
                        
            a = GrabWriteSlice();
            b = GrabWriteSlice();
            passSource = source;
            passDest = a;

            var pm = Engine.ParticleMaterials;

            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => dm.PushRenderTarget(null),
                (dm, _) => dm.PopRenderTarget()
            )) {
                int i = 0;

                foreach (var t in Transforms) {
                    if (!t.IsActive)
                        continue;

                    var spawner = t as Transforms.Spawner;

                    UpdatePass(
                        group, i++, t.GetMaterial(Engine.ParticleMaterials),
                        source, a, b, ref passSource, ref passDest, 
                        startedWhen, spawner, t.SetParameters
                    );
                }

                // FIXME: Is this the right place?
                lock (NewChunks) {
                    foreach (var nc in NewChunks) {
                        SpawnStates[nc.ID] = new SpawnState { Free = 0, Offset = Slice.Chunk.MaximumCount };
                        source.Add(nc);
                    }

                    NewChunks.Clear();
                }

                if (Configuration.DistanceField != null) {
                    UpdatePass(
                        group, i++, pm.UpdateWithDistanceField,
                        source, a, b, ref passSource, ref passDest,
                        startedWhen, null,
                        (p) => {
                            var dfu = new Uniforms.DistanceField(Configuration.DistanceField, Configuration.DistanceFieldMaximumZ);
                            pm.MaterialSet.TrySetBoundUniform(pm.UpdateWithDistanceField, "DistanceField", ref dfu);

                            p["MaximumEncodedDistance"].SetValue(Configuration.DistanceField.MaximumEncodedDistance);
                            p["EscapeVelocity"].SetValue(Configuration.EscapeVelocity);
                            p["BounceVelocityMultiplier"].SetValue(Configuration.BounceVelocityMultiplier);
                            p["LifeDecayRate"].SetValue(Configuration.GlobalLifeDecayRate);
                            p["MaximumVelocity"].SetValue(Configuration.MaximumVelocity);
                            p["CollisionDistance"].SetValue(Configuration.CollisionDistance);
                        }
                    );
                } else {
                    UpdatePass(
                        group, i++, pm.UpdatePositions,
                        source, a, b, ref passSource, ref passDest,
                        startedWhen, null,
                        (p) => {
                            p["LifeDecayRate"].SetValue(Configuration.GlobalLifeDecayRate);
                            p["MaximumVelocity"].SetValue(Configuration.MaximumVelocity);
                        }
                    );
                }

                // ComputeLiveness(group, i++, passSource);
            }

            var ts = Time.Ticks;

            // TODO: Do this immediately after issuing the batch instead?
            Engine.Coordinator.AfterPresent(() => {
                lock (passSource) {
                    // Console.WriteLine("Validate {0}", passSource.Index);
                    passSource.Timestamp = ts;
                    passSource.IsValid = true;
                    passSource.IsBeingGenerated = false;
                }

                a.IsBeingGenerated = false;
                b.IsBeingGenerated = false;

                source.Unlock();
                a.Unlock();
                b.Unlock();

                UnlockedEvent.Set();
            });
        }

        private void RenderChunk (
            BatchGroup group, Slice.Chunk chunk,
            Material m
        ) {
            // TODO: Actual occupied count?
            var quadCount = Slice.Chunk.MaximumCount;

            using (var batch = NativeBatch.New(
                group, chunk.ID, m, (dm, _) => {
                    var p = m.Effect.Parameters;
                    p["PositionTexture"].SetValue(chunk.PositionAndLife);
                    p["VelocityTexture"].SetValue(chunk.Velocity);
                    p["AttributeTexture"].SetValue(chunk.Attributes);
                    m.Flush();
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, 
                    Engine.RasterizeVertexBuffer, 0,
                    Engine.RasterizeOffsetBuffer, 0, 
                    null, 0,
                    Engine.RasterizeIndexBuffer, 0, 0, 4, 0, 2,
                    quadCount
                ));
            }
        }

        public void Render (
            IBatchContainer container, int layer,
            Material material = null,
            Matrix? transform = null, 
            BlendState blendState = null
        ) {
            Slice source;

            var startedWhen = Time.Ticks;

            lock (Slices) {
                source = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).FirstOrDefault();

                // A clear occurred
                if (source == null)
                    return;

                source.Lock("render");
            }

            var m = Engine.Materials.Get(
                material ?? Engine.ParticleMaterials.White, blendState: blendState
            );
            var e = m.Effect;
            var p = e.Parameters;
            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    // TODO: transform arg
                    p["BitmapTexture"].SetValue(Configuration.Texture);
                    p["BitmapTextureRegion"].SetValue(new Vector4(
                        Configuration.TextureRegion.TopLeft, 
                        Configuration.TextureRegion.BottomRight.X, 
                        Configuration.TextureRegion.BottomRight.Y
                    ));
                    p["AnimationRate"].SetValue(Configuration.AnimationRate);
                    p["Size"].SetValue(Configuration.Size / 2);
                    p["VelocityRotation"].SetValue(Configuration.RotationFromVelocity ? 1f : 0f);
                    p["OpacityFromLife"].SetValue(Configuration.OpacityFromLife);
                    p["Texel"].SetValue(new Vector2(1f / Slice.Chunk.Width, 1f / Slice.Chunk.Height));
                },
                (dm, _) => {
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);
                    p["AttributeTexture"].SetValue((Texture2D)null);
                    p["BitmapTexture"].SetValue((Texture2D)null);
                    // ughhhhhhhhhh
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                foreach (var chunk in source)
                    RenderChunk(group, chunk, m);
            }

            // TODO: Do this immediately after issuing the batch instead?
            Engine.Coordinator.AfterPresent(() => {
                source.Unlock();
                UnlockedEvent.Set();
            });
        }

        public void Dispose () {
            foreach (var slice in Slices)
                Engine.Coordinator.DisposeResource(slice);
            foreach (var kvp in LivenessInfos)
                Engine.Coordinator.DisposeResource(kvp.Value);
            foreach (var c in FreeList)
                Engine.Coordinator.DisposeResource(c);
            FreeList.Clear();
            LivenessInfos.Clear();
        }
    }

    public class ParticleSystemConfiguration {
        public readonly int AttributeCount;

        // Configures the sprite rendered for each particle
        public Texture2D Texture;
        public Bounds    TextureRegion = new Bounds(Vector2.Zero, Vector2.One);
        public Vector2   Size = Vector2.One;

        // Animates through the sprite texture based on the particle's life value, if set
        // Smaller values will result in slower animation. Zero turns off animation.
        public Vector2 AnimationRate;

        // If set, particles will rotate based on their direction of movement
        public bool RotationFromVelocity;

        // If != 0, a particle's opacity is equal to its life divided by this value
        public float OpacityFromLife = 0;

        // Life of all particles decreases by this much every update
        public float GlobalLifeDecayRate = 1;

        // If set, particles collide with volumes in this distance field
        public DistanceField DistanceField;
        public float         DistanceFieldMaximumZ;

        // The distance at which a particle is considered colliding with the field.
        // Raise this to make particles 'larger'.
        public float         CollisionDistance = 0.5f;

        // Particles will not be allowed to exceed this velocity
        public float         MaximumVelocity = 9999f;

        // Particles trapped inside distance field volumes will attempt to escape
        //  at this velocity multiplied by their distance from the outside
        public float         EscapeVelocity = 1.0f;
        // Particles colliding with distance field volumes will retain this much
        //  of their speed and bounce off of the volume
        public float         BounceVelocityMultiplier = 0.0f;

        public ParticleSystemConfiguration (
            int attributeCount = 0
        ) {
            AttributeCount = attributeCount;
        }
    }
}