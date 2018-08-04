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
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant.Particles {
    public class ParticleSystem : IDisposable {
        internal struct SpawnState {
            public int Offset, Free;
        }

        internal class LivenessInfo {
            public int            ID;
            public int?           Count;
            public bool           IsQueryRunning;
            public OcclusionQuery PendingQuery;
            public long           LastQueryStart;
            public int            DeadFrameCount;
            public bool           SpawnedParticlesThisFrame;
        }

        internal class ChunkInitializer {
            public ParticleSystem System;
            public int Remaining;
            public BufferInitializer Position, Velocity, Attributes;
            public Slice.Chunk Chunk;
            public bool HasFailed;

            public void Run (ThreadGroup g) {
                if (g != null) {
                    var q = g.GetQueueForType<BufferInitializer>();
                    Position.Parent = Velocity.Parent = Attributes.Parent = this;

                    q.Enqueue(ref Position);
                    q.Enqueue(ref Velocity);
                    if (Attributes.Initializer != null)
                        q.Enqueue(ref Attributes);
                } else {
                    Position.Execute();
                    Velocity.Execute();
                    if (Attributes.Initializer != null)
                        Attributes.Execute();
                }
            }

            public void OnBufferInitialized (bool failed) {
                var result = Interlocked.Decrement(ref Remaining);
                if (failed)
                    HasFailed = true;

                if (result == 0) {
                    if (!failed)
                    lock (System.NewChunks)
                        System.NewChunks.Add(Chunk);
                }
            }
        }

        internal struct BufferInitializer : IWorkItem {
            static ThreadLocal<Vector4[]> Scratch = new ThreadLocal<Vector4[]>();

            public Action<Vector4[], int> Initializer;
            public int Offset;
            public RenderTarget2D Buffer;
            public ChunkInitializer Parent;

            public void Execute () {
                var scratch = Scratch.Value;
                if (scratch == null)
                    Scratch.Value = scratch = new Vector4[Parent.System.ChunkMaximumCount];

                Initializer(scratch, Offset);

                try {
                    lock (Parent.Chunk.Lock) {
                        if (!Parent.Chunk.IsDisposed)
                        lock (Parent.System.Engine.Coordinator.UseResourceLock)
                            Buffer.SetData(scratch);
                    }
                    Parent.OnBufferInitialized(false);
                } catch (ObjectDisposedException) {
                    // This can happen even if we properly synchronize accesses, 
                    //  presumably because the owning graphicsdevice got eaten :(
                    Parent.OnBufferInitialized(true);
                }
            }
        }

        internal class Slice : IDisposable, IEnumerable<Slice.Chunk> {
            public class Chunk : IDisposable {
                public int Size, MaximumCount;
                public int ID;
                public int RefCount;

                public RenderTargetBinding[] Bindings;

                public RenderTarget2D PositionAndLife;
                public RenderTarget2D Velocity;
                public RenderTarget2D Attributes;

                public OcclusionQuery Query;

                public object Lock = new object();

                private bool _IsDisposed = false;

                public Chunk (
                    int id, int size, int attributeCount, GraphicsDevice device
                ) {
                    ID = id;
                    Size = size;
                    MaximumCount = size * size;

                    Bindings = new RenderTargetBinding[2 + attributeCount];
                    Bindings[0] = PositionAndLife = CreateRenderTarget(device);
                    Bindings[1] = Velocity = CreateRenderTarget(device);
                    Query = new OcclusionQuery(device);

                    if (attributeCount == 1)
                        Bindings[2] = Attributes = CreateRenderTarget(device);
                }

                private RenderTarget2D CreateRenderTarget (GraphicsDevice device) {
                    return new RenderTarget2D(
                        device, 
                        Size, Size, false, 
                        SurfaceFormat.Vector4, DepthFormat.None, 
                        0, RenderTargetUsage.PreserveContents
                    );
                }

                public bool IsDisposed {
                    get {
                        return _IsDisposed;
                    }
                }

                public void Dispose () {
                    lock (Lock) {
                        if (_IsDisposed)
                            return;

                        _IsDisposed = true;

                        PositionAndLife.Dispose();
                        Velocity.Dispose();
                        if (Attributes != null)
                            Attributes.Dispose();

                        PositionAndLife = Velocity = Attributes = null;

                        Query.Dispose();
                        Query = null;
                    }
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
            new List<Transforms.ParticleTransform>();

        // 3 because we go
        // old -> a -> b -> a -> ... -> done
        private const int SliceCount          = 3;
        private Slice[] Slices;

        private readonly List<Slice.Chunk> NewChunks = new List<Slice.Chunk>();

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
                return Slices[0].Count * ChunkMaximumCount;
            }
        }

        private LivenessInfo GetLivenessInfo (int id) {
            LivenessInfo result;
            if (LivenessInfos.TryGetValue(id, out result))
                return result;

            LivenessInfos.Add(
                id, result = new LivenessInfo {
                    ID = id,
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
            lock (Engine.FreeList) {
                if (Engine.FreeList.Count > 0) {
                    var result = Engine.FreeList[0];
                    Engine.FreeList.RemoveAt(0);
                    result.ID = id;
                    return result;
                }
            }

            lock (Engine.Coordinator.CreateResourceLock)
                return new Slice.Chunk(id, Engine.Configuration.ChunkSize, Configuration.AttributeCount, device);
        }

        // Make sure to lock the slice first.
        public int InitializeNewChunks (
            int particleCount,
            GraphicsDevice device,
            bool parallel,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            Action<Vector4[], int> attributeInitializer
        ) {
            var mc = ChunkMaximumCount;
            int numToSpawn = (int)Math.Ceiling((double)particleCount / mc);

            var g = parallel ? Engine.Coordinator.ThreadGroup : null;

            for (int i = 0; i < numToSpawn; i++) {
                var c = CreateChunk(device, Interlocked.Increment(ref NextChunkId));
                var offset = i * mc;
                var pos = new BufferInitializer { Buffer = c.PositionAndLife, Initializer = positionInitializer, Offset = offset };
                var vel = new BufferInitializer { Buffer = c.Velocity, Initializer = velocityInitializer, Offset = offset };
                var attr = new BufferInitializer { Buffer = c.Attributes, Initializer = attributeInitializer, Offset = offset };
                var job = new ChunkInitializer {
                    System = this,
                    Position = pos,
                    Velocity = vel,
                    Attributes = attr,
                    Chunk = c,
                    Remaining = (attributeInitializer != null) ? 3 : 2
                };

                job.Run(g);
            }

            return numToSpawn * mc;
        }

        internal int ChunkMaximumCount {
            get {
                return Engine.Configuration.ChunkSize * Engine.Configuration.ChunkSize;
            }
        }

        internal Vector2 ChunkSizeF {
            get {
                return new Vector2(Engine.Configuration.ChunkSize, Engine.Configuration.ChunkSize);
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
                else if (spawnCount > ChunkMaximumCount)
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
                    SpawnStates[spawnId.Value] = new SpawnState { Offset = 0, Free = ChunkMaximumCount };
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
                RenderTrace.Marker(batch, -9999, "Particle transform {0}", m.Effect.CurrentTechnique.Name);

                int i = 0;
                foreach (var sourceChunk in _source) {
                    var destChunk = _dest.GetByID(sourceChunk.ID);

                    var li = GetLivenessInfo(sourceChunk.ID);
                    UpdateChunkLivenessQuery(li);

                    var runQuery = (li.PendingQuery == null) && (spawner == null);

                    if (runQuery) {
                        li.LastQueryStart = Time.Ticks;
                        li.PendingQuery = destChunk.Query;
                    }

                    var chunkMaterial = m;
                    if (spawner != null) {
                        if (sourceChunk.ID != spawnId) {
                            chunkMaterial = Engine.ParticleMaterials.NullTransform;
                        } else {
                            chunkMaterial = m;

                            SpawnState spawnState;
                            if (!SpawnStates.TryGetValue(spawnId.Value, out spawnState))
                                spawnState = new SpawnState { Offset = ChunkMaximumCount, Free = 0 };

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
                        runQuery ? li : null
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
            LivenessInfo li
        ) {
            // Console.WriteLine("{0} -> {1}", passSource.Index, passDest.Index);
            var e = m.Effect;
            var p = e.Parameters;
            using (var batch = NativeBatch.New(
                container, layer, m,
                (dm, _) => {
                    dm.Device.SetRenderTargets(dest.Bindings);
                    dm.Device.Viewport = new Viewport(0, 0, Engine.Configuration.ChunkSize, Engine.Configuration.ChunkSize);

                    /*
                    if (query != null)
                        // For some reason this is a measurable performance hit
                        dm.Device.Clear(Color.Transparent);
                        */

                    p["Texel"].SetValue(new Vector2(1f / Engine.Configuration.ChunkSize, 1f / Engine.Configuration.ChunkSize));

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

                    if (li != null) {
                        // HACK: For some reason this is necessary? It shouldn't be.
                        var temp = li.PendingQuery.IsComplete;

                        Monitor.Enter(li.PendingQuery);
                        li.PendingQuery.Begin();
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

                    if (li != null) {
                        li.PendingQuery.End();
                        Monitor.Exit(li.PendingQuery);
                        li.IsQueryRunning = true;
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
            return Spawn(particleCount, positionInitializer, velocityInitializer, null, parallel);
        }

        public int Spawn (
            int particleCount,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            Action<Vector4[], int> attributeInitializer,
            bool parallel = true
        ) {
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
            if (target.PendingQuery == null)
                return;

            lock (target.PendingQuery) {
                if (!target.IsQueryRunning)
                    return;

                if (!target.PendingQuery.IsComplete)
                    return;

                if (target.PendingQuery.IsDisposed) {
                    target.PendingQuery = null;
                    return;
                }

                target.IsQueryRunning = false;

                if (target.LastQueryStart <= LastClearTimestamp) {
                    target.Count = null;
                    target.DeadFrameCount = 0;
                } else {
                    target.Count = target.PendingQuery.PixelCount;

                    if (target.Count > 0)
                        target.DeadFrameCount = 0;
                }

                target.PendingQuery = null;
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
            lock (Engine.FreeList) {
                if (Engine.FreeList.Count < Engine.Configuration.FreeListCapacity)
                    Engine.FreeList.Add(chunk);
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
                    t.Engine = Engine;
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
                        SpawnStates[nc.ID] = new SpawnState { Free = 0, Offset = ChunkMaximumCount };
                        source.Add(nc);
                    }

                    NewChunks.Clear();
                }

                if (Configuration.DistanceField != null) {
                    if (Configuration.DistanceFieldMaximumZ == null)
                        throw new InvalidOperationException("If a distance field is active, you must set DistanceFieldMaximumZ");

                    UpdatePass(
                        group, i++, pm.UpdateWithDistanceField,
                        source, a, b, ref passSource, ref passDest,
                        startedWhen, null,
                        (p) => {
                            var dfu = new Uniforms.DistanceField(Configuration.DistanceField, Configuration.DistanceFieldMaximumZ.Value);
                            pm.MaterialSet.TrySetBoundUniform(pm.UpdateWithDistanceField, "DistanceField", ref dfu);

                            p["MaximumEncodedDistance"].SetValue(Configuration.DistanceField.MaximumEncodedDistance);
                            p["EscapeVelocity"].SetValue(Configuration.EscapeVelocity);
                            p["BounceVelocityMultiplier"].SetValue(Configuration.BounceVelocityMultiplier);
                            p["LifeDecayRate"].SetValue(Configuration.GlobalLifeDecayRate);
                            p["MaximumVelocity"].SetValue(Configuration.MaximumVelocity);
                            p["CollisionDistance"].SetValue(Configuration.CollisionDistance);
                            p["CollisionLifePenalty"].SetValue(Configuration.CollisionLifePenalty);
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
            var quadCount = ChunkMaximumCount;

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
                    var bt = p["BitmapTexture"];
                    if (bt != null) {
                        bt.SetValue(Configuration.Texture);
                        p["BitmapTextureRegion"].SetValue(new Vector4(
                            Configuration.TextureRegion.TopLeft, 
                            Configuration.TextureRegion.BottomRight.X, 
                            Configuration.TextureRegion.BottomRight.Y
                        ));
                        p["AnimationRate"].SetValue(Configuration.AnimationRate);
                        p["Size"].SetValue(Configuration.Size / 2);
                        p["VelocityRotation"].SetValue(Configuration.RotationFromVelocity ? 1f : 0f);
                        p["Texel"].SetValue(new Vector2(1f / Engine.Configuration.ChunkSize, 1f / Engine.Configuration.ChunkSize));
                    }

                    var zToY = p["ZToY"];
                    if (zToY != null)
                        zToY.SetValue(Configuration.ZToY);

                    p["OpacityFromLife"].SetValue(Configuration.OpacityFromLife);
                },
                (dm, _) => {
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);
                    p["AttributeTexture"].SetValue((Texture2D)null);
                    var bt = p["BitmapTexture"];
                    if (bt != null)
                        bt.SetValue((Texture2D)null);
                    // ughhhhhhhhhh
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                RenderTrace.Marker(group, -9999, "Rasterize {0} particle chunks", source.Count);

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
            LivenessInfos.Clear();
        }
    }

    public class ParticleSystemConfiguration {
        public readonly int  AttributeCount;

        // Configures the sprite rendered for each particle
        public Texture2D     Texture;
        public Bounds        TextureRegion = new Bounds(Vector2.Zero, Vector2.One);
        public Vector2       Size = Vector2.One;

        // Animates through the sprite texture based on the particle's life value, if set
        // Smaller values will result in slower animation. Zero turns off animation.
        public Vector2       AnimationRate;

        // If set, particles will rotate based on their direction of movement
        public bool          RotationFromVelocity;

        // If != 0, a particle's opacity is equal to its life divided by this value
        public float         OpacityFromLife = 0;

        // Life of all particles decreases by this much every update
        public float         GlobalLifeDecayRate = 1;

        // If set, particles collide with volumes in this distance field
        public DistanceField DistanceField;
        public float?        DistanceFieldMaximumZ;

        // The distance at which a particle is considered colliding with the field.
        // Raise this to make particles 'larger'.
        public float         CollisionDistance = 0.5f;

        // Life of a particle decreases by this much every frame if it collides
        //  with or is inside of a volume
        public float         CollisionLifePenalty = 0;

        // Particles will not be allowed to exceed this velocity
        public float         MaximumVelocity = 9999f;

        // Particles trapped inside distance field volumes will attempt to escape
        //  at this velocity multiplied by their distance from the outside
        public float         EscapeVelocity = 1.0f;
        // Particles colliding with distance field volumes will retain this much
        //  of their speed and bounce off of the volume
        public float         BounceVelocityMultiplier = 0.0f;

        // Applies the particle's Z coordinate to its Y coordinate at render time for 2.5D effect
        public float         ZToY = 0;

        public ParticleSystemConfiguration (
            int attributeCount = 0
        ) {
            AttributeCount = attributeCount;
        }
    }
}