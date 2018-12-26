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
using Microsoft.Xna.Framework.Graphics.PackedVector;
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
            public Chunk          Chunk;
            public int?           Count;
            public bool           IsQueryRunning;
            public OcclusionQuery PendingQuery;
            public long           LastQueryStart;
            public int            DeadFrameCount;
        }

        internal class ChunkInitializer<TElement>
            where TElement : struct {
            public ParticleSystem System;
            public int Remaining;
            public BufferInitializer<TElement> Position, Velocity, Attributes;
            public Chunk Chunk;
            public bool HasFailed;

            public void Run (ThreadGroup g) {
                if (g != null) {
                    var q = g.GetQueueForType<BufferInitializer<TElement>>();
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

        internal struct BufferInitializer<TElement> : IWorkItem
            where TElement : struct
        {
            static ThreadLocal<TElement[]> Scratch = new ThreadLocal<TElement[]>();

            public Action<TElement[], int> Initializer;
            public int Offset;
            public RenderTarget2D Buffer;
            public ChunkInitializer<TElement> Parent;

            public void Execute () {
                var scratch = Scratch.Value;
                if (scratch == null)
                    Scratch.Value = scratch = new TElement[Parent.System.ChunkMaximumCount];

                Initializer(scratch, Offset);

                try {
                    if (!Parent.Chunk.IsDisposed)
                    lock (Parent.System.Engine.Coordinator.UseResourceLock)
                        Buffer.SetData(scratch);
                    Parent.OnBufferInitialized(false);
                } catch (ObjectDisposedException) {
                    // This can happen even if we properly synchronize accesses, 
                    //  presumably because the owning graphicsdevice got eaten :(
                    Parent.OnBufferInitialized(true);
                }
            }
        }

        internal class BufferSet : IDisposable {
            public readonly int Size, MaximumCount;
            public readonly int ID;

            public long LastTurnUsed;

            public RenderTargetBinding[] Bindings;
            public RenderTarget2D PositionAndLife;
            public RenderTarget2D Velocity;
            public RenderTarget2D Attributes;

            public bool IsDisposed { get; private set; }

            private static volatile int NextID;

            public BufferSet (ParticleEngineConfiguration configuration, GraphicsDevice device) {
                ID = Interlocked.Increment(ref NextID);
                Size = configuration.ChunkSize;
                MaximumCount = Size * Size;

                Bindings = new RenderTargetBinding[2 + configuration.AttributeCount];

                PositionAndLife = CreateRenderTarget(configuration, device);
                Velocity = CreateRenderTarget(configuration, device);
                if (configuration.AttributeCount > 0)
                    Attributes = CreateRenderTarget(configuration, device);

                Bindings[0] = new RenderTargetBinding(PositionAndLife);
                Bindings[1] = new RenderTargetBinding(Velocity);
                if (configuration.AttributeCount > 0)
                    Bindings[2] = new RenderTargetBinding(Attributes);
            }

            internal RenderTarget2D CreateRenderTarget (ParticleEngineConfiguration configuration, GraphicsDevice device) {
                return new RenderTarget2D(
                    device, 
                    Size, Size, false, 
                    configuration.HighPrecision ? SurfaceFormat.Vector4 : SurfaceFormat.HalfVector4, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                IsDisposed = true;
                PositionAndLife.Dispose();
                Velocity.Dispose();
                if (Attributes != null)
                    Attributes.Dispose();
            }
        }

        public class Chunk : IDisposable {
            public readonly int Size, MaximumCount;
            public int ID;
            public int RefCount;

            internal BufferSet Previous, Current;

            public OcclusionQuery Query;

            public bool IsDisposed { get; private set; }

            private static volatile int NextID;

            public Chunk (
                ParticleEngineConfiguration configuration, GraphicsDevice device
            ) {
                ID = Interlocked.Increment(ref NextID);
                Size = configuration.ChunkSize;
                MaximumCount = Size * Size;

                Query = new OcclusionQuery(device);
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                Query.Dispose();
                Query = null;
            }
        }

        public int LiveCount { get; private set; }

        public readonly ParticleEngine                     Engine;
        public readonly ParticleSystemConfiguration        Configuration;
        public readonly List<Transforms.ParticleTransform> Transforms = 
            new List<Transforms.ParticleTransform>();

        private  readonly List<Chunk> NewChunks = new List<Chunk>();
        internal readonly List<Chunk> Chunks = new List<Chunk>();

        private readonly Dictionary<int, LivenessInfo> LivenessInfos = new Dictionary<int, LivenessInfo>();
        private readonly Dictionary<int, SpawnState> SpawnStates = new Dictionary<int, SpawnState>();

        private int CurrentFrameIndex;

        internal long LastClearTimestamp;
        internal bool IsClearPending;

        private int LastResetCount = 0;
        public event Action<ParticleSystem> OnDeviceReset;

        // HACK: Performing occlusion queries every frame seems to be super unreliable,
        //  so just perform them intermittently and accept that our data will be outdated
        public const int LivenessCheckInterval = 4;
        private int FramesUntilNextLivenessCheck = LivenessCheckInterval;

        private double? LastUpdateTimeSeconds = null;

        private readonly RenderTarget2D LivenessQueryRT;

        /// <summary>
        /// The number of frames a chunk must be dead for before it is reclaimed
        /// </summary>
        public int DeadFrameThreshold = LivenessCheckInterval * 3;

        public ParticleSystem (
            ParticleEngine engine, ParticleSystemConfiguration configuration
        ) {
            Engine = engine;
            Configuration = configuration;
            LiveCount = 0;

            lock (engine.Coordinator.CreateResourceLock)
                LivenessQueryRT = new RenderTarget2D(engine.Coordinator.Device, 1, 1, false, SurfaceFormat.Color, DepthFormat.None);
        }

        public ITimeProvider TimeProvider {
            get {
                return Configuration.TimeProvider ?? (Engine.Configuration.TimeProvider ?? Time.DefaultTimeProvider);
            }
        }

        public int Capacity {
            get {
                // FIXME
                return Chunks.Count * ChunkMaximumCount;
            }
        }

        private LivenessInfo GetLivenessInfo (Chunk chunk) {
            LivenessInfo result;
            if (LivenessInfos.TryGetValue(chunk.ID, out result))
                return result;

            LivenessInfos.Add(
                chunk.ID, result = new LivenessInfo {
                    Chunk = chunk,
                    Count = null
                }
            );
            return result;
        }

        private BufferSet CreateBufferSet (GraphicsDevice device) {
            lock (Engine.Coordinator.CreateResourceLock) {
                var result = new BufferSet(Engine.Configuration, device);
                Engine.AllBuffers.Add(result);
                return result;
            }
        }
        
        private Chunk CreateChunk (GraphicsDevice device) {
            lock (Engine.Coordinator.CreateResourceLock) {
                var result = new Chunk(Engine.Configuration, device);
                result.Current = AcquireOrCreateBufferSet();
                return result;
            }
        }

        // Make sure to lock the slice first.
        public int InitializeNewChunks<TElement> (
            int particleCount,
            GraphicsDevice device,
            bool parallel,
            Action<TElement[], int> positionInitializer,
            Action<TElement[], int> velocityInitializer,
            Action<TElement[], int> attributeInitializer
        ) where TElement : struct {
            var mc = ChunkMaximumCount;
            int numToSpawn = (int)Math.Ceiling((double)particleCount / mc);

            var g = parallel ? Engine.Coordinator.ThreadGroup : null;

            for (int i = 0; i < numToSpawn; i++) {
                var c = CreateChunk(device);
                // Console.WriteLine("Creating new chunk " + c.ID);
                var offset = i * mc;
                var curr = c.Current;
                var pos = new BufferInitializer<TElement> { Buffer = curr.PositionAndLife, Initializer = positionInitializer, Offset = offset };
                var vel = new BufferInitializer<TElement> { Buffer = curr.Velocity, Initializer = velocityInitializer, Offset = offset };
                var attr = new BufferInitializer<TElement> { Buffer = curr.Attributes, Initializer = attributeInitializer, Offset = offset };
                var job = new ChunkInitializer<TElement> {
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
            long startedWhen, Transforms.Spawner spawner,
            Transforms.ParameterSetter setParameters,
            double deltaTimeSeconds, bool clearFirst
        ) {
            var device = container.RenderManager.DeviceManager.Device;

            int? spawnId = null;
            int spawnCount = 0;

            if (spawner != null) {
                spawner.Tick(deltaTimeSeconds, out spawnCount);

                if (spawnCount <= 0)
                    return;
                else if (spawnCount > ChunkMaximumCount)
                    throw new Exception("Spawn count too high to fit in a chunk");

                // FIXME: Inefficient. Spawn across two buffers?
                spawnId = GetSpawnTarget(spawnCount);
                if (spawnId == null) {
                    // HACK
                    spawnId = GetSpawnTarget(spawnCount / 2);

                    if (spawnId.HasValue) {
                        // Console.WriteLine("Partial spawn");
                        spawnCount = Math.Min(SpawnStates[spawnId.Value].Free, spawnCount);
                    }
                }

                if (spawnId == null) {
                    var chunk = CreateChunk(device);
                    spawnId = chunk.ID;
                    SpawnStates[chunk.ID] = new SpawnState { Offset = 0, Free = ChunkMaximumCount };
                    Chunks.Add(chunk);
                }
            }

            var e = m.Effect;
            var p = (e != null) ? e.Parameters : null;

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
                RotateBuffers();

                if (e != null)
                    RenderTrace.Marker(batch, -9999, "Particle transform {0}", e.CurrentTechnique.Name);

                int i = 0;

                foreach (var chunk in Chunks) {
                    var li = GetLivenessInfo(chunk);
                    UpdateChunkLivenessQuery(li);

                    var chunkMaterial = m;
                    if (spawner != null) {
                        if (chunk.ID != spawnId) {
                            chunkMaterial = Engine.ParticleMaterials.NullTransform;
                        } else {
                            chunkMaterial = m;

                            SpawnState spawnState;
                            if (!SpawnStates.TryGetValue(spawnId.Value, out spawnState))
                                spawnState = new SpawnState { Offset = ChunkMaximumCount, Free = 0 };

                            var first = spawnState.Offset;
                            var last = spawnState.Offset + spawnCount - 1;
                            spawner.SetIndices(first, last);
                            // Console.WriteLine("Spawning {0}-{1} free {2}", first, last, spawnState.Free);

                            spawnState.Offset += spawnCount;
                            spawnState.Free -= spawnCount;

                            SpawnStates[spawnId.Value] = spawnState;

                            li.DeadFrameCount = 0;
                        }
                    }

                    ChunkUpdatePass(
                        batch, i++,
                        chunkMaterial, chunk,
                        setParameters,
                        deltaTimeSeconds, clearFirst
                    );
                }
            }
        }

        private void ChunkUpdatePass (
            IBatchContainer container, int layer, Material m,
            Chunk chunk, Transforms.ParameterSetter setParameters,
            double deltaTimeSeconds, bool clearFirst
        ) {
            var prev = chunk.Previous;
            var curr = chunk.Current;

            if (prev != null)
                prev.LastTurnUsed = Engine.CurrentTurn;
            curr.LastTurnUsed = Engine.CurrentTurn;

            var e = m.Effect;
            var p = (e != null) ? e.Parameters : null;
            using (var batch = NativeBatch.New(
                container, layer, m,
                (dm, _) => {
                    var vp = new Viewport(0, 0, Engine.Configuration.ChunkSize, Engine.Configuration.ChunkSize);
                    dm.Device.SetRenderTargets(curr.Bindings);
                    dm.Device.Viewport = vp;

                    if (e != null) {
                        if (setParameters != null)
                            setParameters(Engine, p, CurrentFrameIndex);

                        if (prev != null) {
                            p["PositionTexture"].SetValue(prev.PositionAndLife);
                            p["VelocityTexture"].SetValue(prev.Velocity);

                            var at = p["AttributeTexture"];
                            if (at != null)
                                at.SetValue(prev.Attributes);
                        }

                        var dft = p["DistanceFieldTexture"];
                        if (dft != null)
                            dft.SetValue(Configuration.DistanceField.Texture);

                        var rt = p["RandomnessTexture"];
                        if (rt != null) {
                            p["RandomnessTexel"].SetValue(new Vector2(1.0f / ParticleEngine.RandomnessTextureWidth, 1.0f / ParticleEngine.RandomnessTextureHeight));
                            rt.SetValue(Engine.RandomnessTexture);
                        }

                        SetSystemUniform(m, deltaTimeSeconds);

                        m.Flush();
                    }

                    if (clearFirst)
                        dm.Device.Clear(Color.Transparent);
                },
                (dm, _) => {
                    // XNA effectparameter gets confused about whether a value is set or not, so we do this
                    //  to ensure it always re-sets the texture parameter
                    if (e != null) {
                        p["PositionTexture"].SetValue((Texture2D)null);
                        p["VelocityTexture"].SetValue((Texture2D)null);

                        var rt = p["RandomnessTexture"];
                        if (rt != null)
                            rt.SetValue((Texture2D)null);

                        var at = p["AttributeTexture"];
                        if (at != null)
                            at.SetValue((Texture2D)null);

                        var dft = p["DistanceFieldTexture"];
                        if (dft != null)
                            dft.SetValue((Texture2D)null);
                    }
                }
            )) {
                if (e != null)
                    batch.Add(new NativeDrawCall(
                        PrimitiveType.TriangleList, Engine.TriVertexBuffer, 0,
                        Engine.TriIndexBuffer, 0, 0, Engine.TriVertexBuffer.VertexCount, 0, Engine.TriVertexBuffer.VertexCount / 2
                    ));
            }
        }

        public void Clear () {
            LastClearTimestamp = Time.Ticks;
            IsClearPending = true;
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

        public int Spawn (
            int particleCount,
            Action<HalfVector4[], int> positionInitializer,
            Action<HalfVector4[], int> velocityInitializer,
            bool parallel = true
        ) {
            return Spawn(particleCount, positionInitializer, velocityInitializer, null, parallel);
        }

        public int Spawn (
            int particleCount,
            Action<HalfVector4[], int> positionInitializer,
            Action<HalfVector4[], int> velocityInitializer,
            Action<HalfVector4[], int> attributeInitializer,
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
                    // Console.WriteLine("Chunk " + target.ID + " " + target.Count);

                    if (target.Count > 0)
                        target.DeadFrameCount = 0;
                }

                target.PendingQuery = null;
            }
        }

        private void UpdateLivenessAndReapDeadChunks () {
            LiveCount = 0;

            foreach (var kvp in LivenessInfos) {
                var isDead = false;
                var li = kvp.Value;
                UpdateChunkLivenessQuery(li);
                LiveCount += li.Count.GetValueOrDefault(0);

                if (li.Count.GetValueOrDefault(1) <= 0) {
                    li.DeadFrameCount++;
                    if (li.DeadFrameCount >= DeadFrameThreshold) {
                        // Console.WriteLine("Chunk " + li.Chunk.ID + " dead");
                        isDead = true;
                    }
                }

                if (isDead)
                    ChunksToReap.Add(li);
            }

            foreach (var li in ChunksToReap) {
                SpawnStates.Remove(li.Chunk.ID);
                LivenessInfos.Remove(li.Chunk.ID);
                Reap(li.Chunk);
            }

            ChunksToReap.Clear();
        }

        private void Reap (BufferSet buffer) {
            if (buffer == null)
                return;
            Engine.DiscardedBuffers.Add(buffer);
        }

        private void Reap (Chunk chunk) {
            // Console.WriteLine("Chunk reaped");
            Reap(chunk.Previous);
            Reap(chunk.Current);
            chunk.Previous = chunk.Current = null;
            Chunks.Remove(chunk);
            Engine.Coordinator.DisposeResource(chunk);
        }

        private void SetSystemUniform (Material m, double deltaTimeSeconds) {
            var psu = new Uniforms.ParticleSystem(Engine.Configuration, Configuration, deltaTimeSeconds);
            Engine.ParticleMaterials.MaterialSet.TrySetBoundUniform(m, "System", ref psu);
        }

        private BufferSet AcquireOrCreateBufferSet () {
            BufferSet result;
            if (!Engine.AvailableBuffers.TryPopFront(out result))
                result = CreateBufferSet(Engine.Coordinator.Device);
            result.LastTurnUsed = Engine.CurrentTurn;
            return result;
        }

        private void RotateBuffers () {
            Engine.NextTurn();

            foreach (var chunk in Chunks) {
                var prev = chunk.Previous;
                chunk.Previous = chunk.Current;
                chunk.Current = AcquireOrCreateBufferSet();
                if (prev != null)
                    Engine.DiscardedBuffers.Add(prev);
            }
        }

        private void ClearAndDiscard (BatchGroup g, BufferSet set) {
            if (set == null)
                return;
            using (var rtg = BatchGroup.ForRenderTarget(g, 0, set.PositionAndLife))
                ClearBatch.AddNew(rtg, 0, Engine.Materials.Clear, clearColor: Color.Transparent);
            Engine.DiscardedBuffers.Add(set);
        }

        public void Update (IBatchContainer container, int layer, float? deltaTimeSeconds = null) {
            var lastUpdateTimeSeconds = LastUpdateTimeSeconds;
            LastUpdateTimeSeconds = TimeProvider.Seconds;
            CurrentFrameIndex++;

            float actualDeltaTimeSeconds = 1 / 60f;
            if (deltaTimeSeconds.HasValue)
                actualDeltaTimeSeconds = deltaTimeSeconds.Value;
            else if (lastUpdateTimeSeconds.HasValue)
                actualDeltaTimeSeconds = (float)Math.Min(
                    LastUpdateTimeSeconds.Value - lastUpdateTimeSeconds.Value, 
                    Engine.Configuration.MaximumUpdateDeltaTimeSeconds
                );

            var startedWhen = Time.Ticks;

            if (LastResetCount != Engine.ResetCount) {
                if (OnDeviceReset != null)
                    OnDeviceReset(this);
                LastResetCount = Engine.ResetCount;
            }

            lock (LivenessInfos)
                UpdateLivenessAndReapDeadChunks();

            var initialTurn = Engine.CurrentTurn;

            var pm = Engine.ParticleMaterials;

            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => dm.PushRenderTarget(null),
                (dm, _) => dm.PopRenderTarget()
            )) {
                int i = 0;

                lock (NewChunks) {
                    foreach (var nc in NewChunks) {
                        SpawnStates[nc.ID] = new SpawnState { Free = 0, Offset = ChunkMaximumCount };
                        Chunks.Add(nc);
                    }

                    NewChunks.Clear();
                }

                var isFirstXform = true;
                foreach (var t in Transforms) {
                    var it = (Transforms.IParticleTransform)t;
                    var spawner = t as Transforms.Spawner;

                    var shouldSkip = !t.IsActive;
                    if (shouldSkip)
                        continue;

                    UpdatePass(
                        group, i++, it.GetMaterial(Engine.ParticleMaterials),
                        startedWhen, spawner, it.SetParameters, 
                        actualDeltaTimeSeconds, isFirstXform
                    );
                    isFirstXform = false;
                }

                if (IsClearPending) {
                    // occlusion queries suck and never work right, and for some reason
                    //  the old particle data is a ghost from hell and refuses to disappear
                    //  even after it is cleared
                    for (int k = 0; k < 3; k++) {
                        UpdatePass(
                            group, i++, pm.Erase,
                            startedWhen, null,
                            null, actualDeltaTimeSeconds, true
                        );
                    }
                    IsClearPending = false;
                } else if (Configuration.DistanceField != null) {
                    if (Configuration.DistanceFieldMaximumZ == null)
                        throw new InvalidOperationException("If a distance field is active, you must set DistanceFieldMaximumZ");

                    UpdatePass(
                        group, i++, pm.UpdateWithDistanceField,
                        startedWhen, null,
                        (Engine, p, frameIndex) => {
                            var dfu = new Uniforms.DistanceField(Configuration.DistanceField, Configuration.DistanceFieldMaximumZ.Value);
                            pm.MaterialSet.TrySetBoundUniform(pm.UpdateWithDistanceField, "DistanceField", ref dfu);
                        }, actualDeltaTimeSeconds, true
                    );
                } else {
                    UpdatePass(
                        group, i++, pm.UpdatePositions,
                        startedWhen, null,
                        null, actualDeltaTimeSeconds, true
                    );
                }

                if (FramesUntilNextLivenessCheck-- <= 0) {
                    FramesUntilNextLivenessCheck = LivenessCheckInterval;
                    ComputeLiveness(group, i++);
                }
            }

            var ts = Time.Ticks;

            Engine.EndOfUpdate(initialTurn);
        }

        private void ComputeLiveness (
            BatchGroup group, int layer
        ) {
            var quadCount = ChunkMaximumCount;

            using (var rtg = BatchGroup.ForRenderTarget(
                group, layer, LivenessQueryRT, (dm, _) => {
                    dm.Device.Clear(Color.White);
                    dm.Device.Clear(Color.Transparent);
                }
            )) {
                RenderTrace.Marker(rtg, -9999, "Perform chunk occlusion queries");

                var m = Engine.ParticleMaterials.CountLiveParticles;

                foreach (var chunk in Chunks) {
                    var li = GetLivenessInfo(chunk);
                    if (li == null)
                        continue;

                    var q = chunk.Query;
                    using (var chunkBatch = NativeBatch.New(
                        rtg, chunk.ID, m, (dm, _) => {
                            SetSystemUniform(m, 0);

                            var p = m.Effect.Parameters;
                            p["PositionTexture"].SetValue(chunk.Current.PositionAndLife);
                            m.Flush();
                            if (!q.IsDisposed) {
                                li.LastQueryStart = Time.Ticks;
                                li.PendingQuery = q;
                                var temp = q.IsComplete;
                                Monitor.Enter(q);
                                q.Begin();
                            }
                        }, (dm, _) => {
                            q.End();
                            Monitor.Exit(q);
                            li.IsQueryRunning = true;
                        }
                    )) {
                        chunkBatch.Add(new NativeDrawCall(
                            PrimitiveType.TriangleList, 
                            Engine.RasterizeVertexBuffer, 0,
                            Engine.RasterizeOffsetBuffer, 0, 
                            null, 0,
                            Engine.RasterizeIndexBuffer, 0, 0, 4, 0, 2,
                            quadCount
                        ));
                    }
                }
            }
        }

        private void RenderChunk (
            BatchGroup group, Chunk chunk, Material m
        ) {
            // TODO: Actual occupied count?
            var quadCount = ChunkMaximumCount;
            var curr = chunk.Current;

            if (curr == null)
                return;

            curr.LastTurnUsed = Engine.CurrentTurn;

            // Console.WriteLine("Draw {0}", chunk.ID);

            using (var batch = NativeBatch.New(
                group, chunk.ID, m, (dm, _) => {
                    var p = m.Effect.Parameters;
                    p["PositionTexture"].SetValue(curr.PositionAndLife);
                    p["VelocityTexture"].SetValue(curr.Velocity);
                    p["AttributeTexture"].SetValue(curr.Attributes);
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
            BlendState blendState = null,
            float? overrideStippleFactor = null
        ) {
            var startedWhen = Time.Ticks;

            var appearance = Configuration.Appearance;
            var lifeRamp = Configuration.Color.LifeRampTexture;
            if (appearance.Texture != null)
                appearance.Texture.EnsureInitialized(Engine.Configuration.TextureLoader);
            if (lifeRamp != null)
                lifeRamp.EnsureInitialized(Engine.Configuration.FPTextureLoader);

            if (material == null) {
                if ((appearance.Texture != null) && (appearance.Texture.Instance != null)) {
                    material = Engine.Configuration.AttributeCount > 0
                        ? Engine.ParticleMaterials.AttributeColor
                        : Engine.ParticleMaterials.White;
                } else {
                    material = Engine.Configuration.AttributeCount > 0
                        ? Engine.ParticleMaterials.AttributeColorNoTexture
                        : Engine.ParticleMaterials.WhiteNoTexture;
                }
            }

            var m = Engine.Materials.Get(
                material, blendState: blendState
            );
            var e = m.Effect;
            var p = e.Parameters;
            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    // FIXME: deltaTime
                    SetSystemUniform(m, 0);

                    // TODO: transform arg
                    var bt = p["BitmapTexture"];
                    if (bt != null) {
                        bt.SetValue(appearance.Texture);
                        p["BitmapTextureRegion"].SetValue(new Vector4(
                            appearance.Region.TopLeft, 
                            appearance.Region.BottomRight.X, 
                            appearance.Region.BottomRight.Y
                        ));
                    }

                    p["AnimationRateAndRotationAndZToY"].SetValue(new Vector4(
                        Configuration.AnimationRate.X, Configuration.AnimationRate.Y,
                        Configuration.RotationFromVelocity ? 1f : 0f, Configuration.ZToY
                    ));

                    p["StippleFactor"].SetValue(overrideStippleFactor.GetValueOrDefault(Configuration.StippleFactor));

                    var gc = p["GlobalColor"];
                    if (gc != null) {
                        var gcolor = Configuration.Color.Global;
                        gcolor.X *= gcolor.W;
                        gcolor.Y *= gcolor.W;
                        gcolor.Z *= gcolor.W;
                        gc.SetValue(gcolor);
                    }

                    var rt = p["LifeRampTexture"];
                    if (rt != null) {
                        var lifeRampTexture =
                            (lifeRamp != null)
                                ? lifeRamp.Instance
                                : null;
                        rt.SetValue(lifeRampTexture);
                        var min = Configuration.Color.LifeRampMinimum;
                        var rangeSize = Math.Max(Configuration.Color.LifeRampMaximum - min, 0.001f);
                        var strength = lifeRampTexture != null
                                ? Configuration.Color.LifeRampStrength
                                : 0;
                        var indexDivisor = lifeRampTexture != null
                            ? lifeRampTexture.Height
                            : 1;
                        p["LifeRampSettings"].SetValue(new Vector4(
                            strength * (Configuration.Color.InvertLifeRamp ? -1 : 1),
                            min, rangeSize, indexDivisor
                        ));
                    }
                },
                (dm, _) => {
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);
                    p["AttributeTexture"].SetValue((Texture2D)null);
                    var rt = p["LifeRampTexture"];
                    if (rt != null)
                        rt.SetValue((Texture2D)null);
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
                RenderTrace.Marker(group, -9999, "Rasterize {0} particle chunks", Chunks.Count);

                foreach (var chunk in Chunks)
                    RenderChunk(group, chunk, m);
            }
        }

        public void Dispose () {
            // FIXME: Release buffers
            foreach (var chunk in Chunks) {
                if (chunk.Previous != null)
                    Engine.DiscardedBuffers.Add(chunk.Previous);
                if (chunk.Current != null)
                    Engine.DiscardedBuffers.Add(chunk.Current);
                Engine.Coordinator.DisposeResource(chunk);
            }
            Engine.Coordinator.DisposeResource(LivenessQueryRT);
            LivenessInfos.Clear();
        }
    }

    public class ParticleAppearance {
        /// <summary>
        /// Configures the sprite used to render each particle.
        /// If null, the particle will be a solid-color quad
        /// </summary>
        public NullableLazyResource<Texture2D> Texture = new NullableLazyResource<Texture2D>();
        /// <summary>
        /// Configures the region of the texture used by the particle. If you specify a subregion the region
        ///  will scroll as the particle animates.
        /// </summary>
        public Bounds Region = Bounds.Unit;
    }

    public class ParticleColor {
        internal Vector4? _ColorFromLife = null;
        [NonSerialized]
        private float?    _OpacityFromLife = null;

        /// <summary>
        /// Sets a global multiply color to apply to the white and attributecolor materials
        /// </summary>
        public Vector4    Global = Vector4.One;

        /// <summary>
        /// Specifies a color ramp texture
        /// </summary>
        public NullableLazyResource<Texture2D> LifeRampTexture;

        /// <summary>
        /// Life values below this are treated as zero
        /// </summary>
        public float LifeRampMinimum = 0.0f;

        /// <summary>
        /// Life values above this are treated as one
        /// </summary>
        public float LifeRampMaximum = 100f;

        /// <summary>
        /// Blends between the constant color value for the particle and the color
        ///  from its life ramp
        /// </summary>
        public float LifeRampStrength = 1.0f;

        /// <summary>
        /// If set, the life ramp has its maximum value at the left instead of the right.
        /// </summary>
        public bool  InvertLifeRamp;

        /// <summary>
        /// Multiplies the particle's opacity, producing a fade-in or fade-out based on the particle's life
        /// </summary>
        public float? OpacityFromLife {
            set {
                if (value == _OpacityFromLife)
                    return;

                if (value != null) {
                    _OpacityFromLife = value.Value;
                    _ColorFromLife = new Vector4(0, 0, 0, value.Value);
                } else {
                    _OpacityFromLife = null;
                    _ColorFromLife = null;
                }
            }
            get {
                if (_OpacityFromLife.HasValue)
                    return _OpacityFromLife.Value;
                else
                    return null;
            }
        }

        /// <summary>
        /// Multiplies the particle's color, producing a fade-in or fade-out based on the particle's life
        /// </summary>
        public Vector4? FromLife {
            get {
                if (_OpacityFromLife.HasValue)
                    return null;
                else
                    return _ColorFromLife;
            }
            set {
                _ColorFromLife = value;
                _OpacityFromLife = null;
            }
        }
    }

    public class ParticleSystemConfiguration {
        /// <summary>
        /// Used to measure elapsed time automatically for updates
        /// </summary>
        [NonSerialized]
        public ITimeProvider TimeProvider = null;

        /// <summary>
        /// Configures the texture used when drawing particles (if any)
        /// </summary>
        public ParticleAppearance Appearance = new ParticleAppearance();

        /// <summary>
        /// Configures the color of particles
        /// </summary>
        public ParticleColor Color = new ParticleColor();

        /// <summary>
        /// The on-screen size of each particle, in pixels
        /// </summary>
        public Vector2       Size = Vector2.One;

        /// <summary>
        /// Animates through the sprite texture based on the particle's life value, if set
        /// Smaller values will result in slower animation. Zero turns off animation.
        /// </summary>
        public Vector2       AnimationRate;

        /// <summary>
        /// If set, particles will rotate based on their direction of movement
        /// </summary>
        public bool          RotationFromVelocity;

        /// <summary>
        /// Multiplies the particle's size, producing a shrink or grow based on the particle's life
        /// </summary>
        public Vector2?      SizeFromLife = null;

        /// <summary>
        /// Life of all particles decreases by this much every update
        /// </summary>
        public float         GlobalLifeDecayRate = 1;

        /// <summary>
        /// If set, particles collide with volumes in this distance field
        /// </summary>
        [NonSerialized]
        public DistanceField DistanceField;
        [NonSerialized]
        public float?        DistanceFieldMaximumZ;

        /// <summary>
        /// The distance at which a particle is considered colliding with the field.
        /// Raise this to make particles 'larger'.
        /// </summary>
        public float         CollisionDistance = 0.33f;

        /// <summary>
        /// Life of a particle decreases by this much every frame if it collides
        ///  with or is inside of a volume
        /// </summary>
        public float         CollisionLifePenalty = 0;

        /// <summary>
        /// Particles will not be allowed to exceed this velocity
        /// </summary>
        public float         MaximumVelocity = 9999f;

        /// <summary>
        /// All particles will have their velocity reduced to roughly Velocity * (1.0 - Friction) every second
        /// </summary>
        public float         Friction = 0f;

        /// <summary>
        /// Particles trapped inside distance field volumes will attempt to escape
        ///  at this velocity multiplied by their distance from the outside
        /// </summary>
        public float         EscapeVelocity = 128.0f;

        /// <summary>
        /// Particles colliding with distance field volumes will retain this much
        ///  of their speed and bounce off of the volume
        /// </summary>
        public float         BounceVelocityMultiplier = 0.0f;

        /// <summary>
        /// Applies the particle's Z coordinate to its Y coordinate at render time for 2.5D effect
        /// </summary>
        public float         ZToY = 0;

        /// <summary>
        /// Coarse-grained control over the number of particles actually rendered
        /// </summary>
        [NonSerialized]
        public float         StippleFactor = 1.0f;

        /// <summary>
        /// Makes particles spin based on their life value
        /// </summary>
        public float         RotationFromLife = 0;

        /// <summary>
        /// Gives particles a constant rotation based on their index (pseudorandom-ish)
        /// </summary>
        public float         RotationFromIndex = 0;

        public ParticleSystemConfiguration () {
        }

        public ParticleSystemConfiguration Clone () {
            var result = (ParticleSystemConfiguration)this.MemberwiseClone();
            return result;
        }
    }
}