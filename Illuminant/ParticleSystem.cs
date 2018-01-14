using System;
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

        private class Slice : IDisposable {
            public class Chunk : IDisposable {
                public const int Width = 256;
                public const int Height = 256;
                public const int MaximumCount = Width * Height;

                public int ID;

                public RenderTargetBinding[] Bindings;

                public RenderTarget2D PositionAndBirthTime;
                public RenderTarget2D Velocity;
                public RenderTarget2D Attributes;

                public Chunk (
                    int id, int attributeCount, GraphicsDevice device
                ) {
                    ID = id;

                    Bindings = new RenderTargetBinding[2 + attributeCount];
                    Bindings[0] = PositionAndBirthTime = CreateRenderTarget(device);
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
                        PositionAndBirthTime.SetData(buf);
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
                    PositionAndBirthTime.Dispose();
                    Velocity.Dispose();
                    if (Attributes != null)
                        Attributes.Dispose();

                    PositionAndBirthTime = Velocity = Attributes = null;
                }
            }

            public readonly int Index;
            public readonly int AttributeCount;
            public long Timestamp;
            public bool IsValid, IsBeingGenerated;
            public int  InUseCount;

            private readonly List<Chunk> Chunks = new List<Chunk>();

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

                lock (Chunks)
                foreach (var c in Chunks)
                    c.Dispose();
            }            

            public Chunk RemoveAt (int index) {
                lock (Chunks) {
                    if ((index >= Chunks.Count) || (index < 0))
                        return null;

                    var chunk = Chunks[index];
                    Chunks.RemoveAt(index);

                    return chunk;
                }
            }

            public Chunk RemoveByID (int id) {
                lock (Chunks) {
                    for (int i = 0; i < Chunks.Count; i++) {
                        var chunk = Chunks[i];
                        if (chunk.ID == id) {
                            Chunks.RemoveAt(i);
                            return chunk;
                        }                            
                    }

                    return null;
                }
            }

            public void Add (Chunk chunk) {
                lock (Chunks) {
                    Chunks.Add(chunk);
                }
            }

            public Chunk GetByID (int id) {
                lock (Chunks) {
                    foreach (var c in Chunks)
                        if (c.ID == id)
                            return c;
                }

                return null;
            }

            public Chunk this [int index] {
                get {
                    lock (Chunks) {
                        if (index >= Chunks.Count)
                            return null;

                        return Chunks[index];
                    }
                }
            }

            public int Count {
                get {
                    lock (Chunks)
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

        private const int FreeListCapacity = 8;

        private readonly List<Slice.Chunk> NewChunks = new List<Slice.Chunk>();
        private readonly List<Slice.Chunk> FreeList = new List<Slice.Chunk>();

        private readonly Dictionary<int, LivenessInfo> LivenessInfos = new Dictionary<int, LivenessInfo>();
        private readonly Dictionary<int, SpawnState> SpawnStates = new Dictionary<int, SpawnState>();

        private readonly IndexBuffer  QuadIndexBuffer;
        private readonly VertexBuffer QuadVertexBuffer;
        private          IndexBuffer  RasterizeIndexBuffer;
        private          VertexBuffer RasterizeVertexBuffer;
        private          VertexBuffer RasterizeOffsetBuffer;

        internal const int            RandomnessTextureWidth = 2048,
                                      RandomnessTextureHeight = 16;
        private             Texture2D RandomnessTexture;

        private readonly AutoResetEvent UnlockedEvent = new AutoResetEvent(true);

        private static readonly short[] QuadIndices = new short[] {
            0, 1, 3, 1, 2, 3
        };

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

                QuadIndexBuffer = new IndexBuffer(engine.Coordinator.Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
                QuadIndexBuffer.SetData(QuadIndices);

                const float argh = 102400;

                QuadVertexBuffer = new VertexBuffer(engine.Coordinator.Device, typeof(ParticleSystemVertex), 4, BufferUsage.WriteOnly);
                QuadVertexBuffer.SetData(new [] {
                    // HACK: Workaround for Intel's terrible video drivers.
                    // No, I don't know why.
                    new ParticleSystemVertex(-argh, -argh, 0),
                    new ParticleSystemVertex(argh, -argh, 1),
                    new ParticleSystemVertex(argh, argh, 2),
                    new ParticleSystemVertex(-argh, argh, 3)
                });

                FillIndexBuffer();
                FillVertexBuffer();
                GenerateRandomnessTexture();

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

        private void FillIndexBuffer () {
            var buf = new short[] {
                0, 1, 3, 1, 2, 3
            };
            RasterizeIndexBuffer = new IndexBuffer(
                Engine.Coordinator.Device, IndexElementSize.SixteenBits, 
                buf.Length, BufferUsage.WriteOnly
            );
            RasterizeIndexBuffer.SetData(buf);
        }

        private void FillVertexBuffer () {
            {
                var buf = new ParticleSystemVertex[4];
                int i = 0;
                var v = new ParticleSystemVertex();
                buf[i++] = v;
                v.Corner = v.Unused = 1;
                buf[i++] = v;
                v.Corner = v.Unused = 2;
                buf[i++] = v;
                v.Corner = v.Unused = 3;
                buf[i++] = v;

                RasterizeVertexBuffer = new VertexBuffer(
                    Engine.Coordinator.Device, typeof(ParticleSystemVertex),
                    buf.Length, BufferUsage.WriteOnly
                );
                RasterizeVertexBuffer.SetData(buf);
            }

            {
                var buf = new ParticleOffsetVertex[Slice.Chunk.MaximumCount];

                for (var y = 0; y < Slice.Chunk.Height; y++) {
                    for (var x = 0; x < Slice.Chunk.Width; x++) {
                        var i = (y * Slice.Chunk.Width) + x;
                        buf[i].Offset = new Vector2(x / (float)Slice.Chunk.Width, y / (float)Slice.Chunk.Height);
                    }
                }

                RasterizeOffsetBuffer = new VertexBuffer(
                    Engine.Coordinator.Device, typeof(ParticleOffsetVertex),
                    buf.Length, BufferUsage.WriteOnly
                );
                RasterizeOffsetBuffer.SetData(buf);
            }
        }

        private void GenerateRandomnessTexture () {
            lock (Engine.Coordinator.CreateResourceLock) {
                // TODO: HalfVector4?
                RandomnessTexture = new Texture2D(
                    Engine.Coordinator.Device,
                    RandomnessTextureWidth, RandomnessTextureHeight, false,
                    SurfaceFormat.Vector4
                );

                var buffer = new Vector4[RandomnessTextureWidth * RandomnessTextureHeight];
                var rng = new MersenneTwister();

                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = new Vector4(rng.NextSingle(), rng.NextSingle(), rng.NextSingle(), rng.NextSingle());

                RandomnessTexture.SetData(buffer);
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

        private int NextChunkId = 1;
        
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
            var idBase = NextChunkId;
            NextChunkId += numToSpawn;

            if (parallel) {
                Parallel.For(
                    0, numToSpawn,
                    (i) => {
                        var c = CreateChunk(device, idBase + i);
                        c.Initialize(i * mc, positionInitializer, velocityInitializer, attributeInitializer);
                        lock (NewChunks)
                            NewChunks.Add(c);
                    }
                );
            } else {
                for (int i = 0; i < numToSpawn; i++) {
                    var c = CreateChunk(device, idBase + i);
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
            bool runOcclusionQuery, long startedWhen, Transforms.Spawner spawner,
            Action<EffectParameterCollection> setParameters
        ) {
            var _source = passSource;
            var _dest = passDest;
            var device = container.RenderManager.DeviceManager.Device;

            for (int i = 0, c = _source.Count; i < c; i++) {
                var chunk = _source[i];
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
                    lock (container.RenderManager.CreateResourceLock) {
                        spawnId = NextChunkId;
                        var temp = CreateChunk(device, spawnId.Value);
                        _dest.Add(temp);
                        NextChunkId++;
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
                if (spawner != null) {
                    var li = GetLivenessInfo(spawnId.Value);

                    var sourceChunk = _source.GetByID(spawnId.Value);
                    var destChunk = _dest.GetByID(spawnId.Value);

                    SpawnState spawnState;
                    if (!SpawnStates.TryGetValue(spawnId.Value, out spawnState))
                        spawnState = new SpawnState { Offset = 0, Free = Slice.Chunk.MaximumCount };

                    spawner.SetIndices(spawnState.Offset, spawnState.Offset + spawnCount);

                    ChunkUpdatePass(
                        batch, 0,
                        m, sourceChunk, destChunk,
                        setParameters, null
                    );

                    spawnState.Offset += spawnCount;
                    spawnState.Free -= spawnCount;
                    if (spawnState.Free < 0)
                        throw new Exception();

                    SpawnStates[spawnId.Value] = spawnState;
                } else {
                    for (int i = 0, l = _source.Count; i < l; i++) {
                        var sourceChunk = _source[i];
                        var destChunk = _dest[i];

                        if (sourceChunk.ID != destChunk.ID)
                            throw new Exception();

                        var li = GetLivenessInfo(sourceChunk.ID);
                        bool runLocal = runOcclusionQuery;

                        UpdateChunkLivenessQuery(li);
                        if (li.IsQueryPending)
                            runLocal = false;

                        if (runLocal) {
                            li.LastQueryStart = Time.Ticks;
                            li.IsQueryPending = true;
                        }

                        ChunkUpdatePass(
                            batch, i,
                            m, sourceChunk, destChunk,
                            setParameters, 
                            runLocal ? li.Query : null
                        );
                    }
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
                    dm.PushRenderTargets(dest.Bindings);
                    dm.Device.Viewport = new Viewport(0, 0, Slice.Chunk.Width, Slice.Chunk.Height);
                    dm.Device.Clear(Color.Transparent);
                    p["Texel"].SetValue(new Vector2(1f / Slice.Chunk.Width, 1f / Slice.Chunk.Height));

                    if (setParameters != null)
                        setParameters(p);

                    if (source != null) {
                        p["PositionTexture"].SetValue(source.PositionAndBirthTime);
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
                        p["RandomnessTexel"].SetValue(new Vector2(1.0f / RandomnessTexture.Width, 1.0f / RandomnessTexture.Height));
                        rt.SetValue(RandomnessTexture);
                    }

                    m.Flush();

                    if (query != null) {
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

                    dm.PopRenderTarget();
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, QuadVertexBuffer, 0,
                    QuadIndexBuffer, 0, 0, QuadVertexBuffer.VertexCount, 0, QuadVertexBuffer.VertexCount / 2
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
            Slice target;

            lock (Slices) {
                target = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).FirstOrDefault() ?? Slices[0];
            }

            target.Lock("initialize");
            lock (target) {
                target.IsValid = false;
                target.IsBeingGenerated = true;
            }

            var result = InitializeNewChunks(
                particleCount,
                Engine.Coordinator.Device,
                parallel,
                positionInitializer,
                velocityInitializer,
                attributeInitializer
            );

            lock (target) {
                target.Timestamp = Time.Ticks;
                target.IsValid = true;
                target.IsBeingGenerated = false;
            }
            target.Unlock();

            return result;
        }

        internal HashSet<LivenessInfo> ChunksToReap = new HashSet<LivenessInfo>();

        private void UpdateChunkLivenessQuery (LivenessInfo target) {
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
                else
                    target.DeadFrameCount++;

                if (target.DeadFrameCount >= 4)
                    ChunksToReap.Add(target);
            }
        }

        private void UpdateLivenessAndReapDeadChunks () {
            LiveCount = 0;

            foreach (var kvp in LivenessInfos) {
                UpdateChunkLivenessQuery(kvp.Value);
                LiveCount += kvp.Value.Count.GetValueOrDefault(0);
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

            lock (NewChunks) {
                foreach (var nc in NewChunks)
                    source.Add(nc);

                NewChunks.Clear();
            }

            a = GrabWriteSlice();
            b = GrabWriteSlice();
            passSource = source;
            passDest = a;

            var pm = Engine.ParticleMaterials;

            using (var group = BatchGroup.New(
                container, layer
            )) {
                int i = 0;
                bool occlusionQueryPending = true;

                foreach (var t in Transforms) {
                    if (!t.IsActive)
                        continue;

                    var spawner = t as Transforms.Spawner;
                    var runQuery = occlusionQueryPending && (spawner == null);

                    UpdatePass(
                        group, i++, t.GetMaterial(Engine.ParticleMaterials),
                        source, a, b, ref passSource, ref passDest, 
                        runQuery, startedWhen, spawner,
                        t.SetParameters
                    );

                    if (runQuery)
                        occlusionQueryPending = false;
                }

                if (Configuration.DistanceField != null) {
                    UpdatePass(
                        group, i++, pm.UpdateWithDistanceField,
                        source, a, b, ref passSource, ref passDest,
                        occlusionQueryPending, startedWhen, null,
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
                        occlusionQueryPending, startedWhen, null,
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
                    p["PositionTexture"].SetValue(chunk.PositionAndBirthTime);
                    p["VelocityTexture"].SetValue(chunk.Velocity);
                    p["AttributeTexture"].SetValue(chunk.Attributes);
                    m.Flush();
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, 
                    RasterizeVertexBuffer, 0,
                    RasterizeOffsetBuffer, 0, 
                    null, 0,
                    RasterizeIndexBuffer, 0, 0, 4, 0, 2,
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
                for (int i = 0, c = source.Count; i < c; i++) {
                    var chunk = source[i];
                    RenderChunk(group, chunk, m);
                }
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
            LivenessInfos.Clear();
            Engine.Coordinator.DisposeResource(RandomnessTexture);
        }
    }

    public class ParticleSystemConfiguration {
        public readonly int AttributeCount;

        // Particles that reach this age are killed
        // Defaults to (effectively) not killing particles
        public int MaximumAge = 1024 * 1024 * 8;

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