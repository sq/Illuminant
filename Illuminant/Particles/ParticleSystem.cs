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
using Squared.Illuminant.Uniforms;
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant.Particles {
    /// <summary>
    /// Represents one or more particle systems
    /// </summary>
    public interface IParticleSystems : IDisposable {
        ParticleSystem this [int index] { get; }
        int SystemCount { get; }

        void Update (IBatchContainer container, int layer);
        void Render (
            IBatchContainer container, int layer,
            Material material = null,
            Matrix? transform = null,
            BlendState blendState = null,
            ParticleRenderParameters renderParams = null,
            bool usePreviousData = false
        );
    }

    public class ParticleSystem : IParticleSystems {
        public const int MaxChunkCount = 128;

        internal class InternalRenderParameters {
            public DefaultMaterialSet DefaultMaterialSet;
            public Material Material;
            public ParticleRenderParameters UserParameters;
        }

        public class UpdateResult {
            public ParticleSystem System { get; private set; }
            public bool PerformedUpdate { get; private set; }
            public float Timestamp { get; private set; }

            internal UpdateResult (ParticleSystem system, bool performedUpdate, float timestamp) {
                System = system;
                PerformedUpdate = performedUpdate;
                Timestamp = timestamp;
            }

            public Future<ArraySegment<BitmapDrawCall>> ReadbackResult {
                get {
                    if (!System.Configuration.AutoReadback)
                        return new Future<ArraySegment<BitmapDrawCall>>(new ArraySegment<BitmapDrawCall>());

                    lock (System.ReadbackLock)
                        return System.ReadbackFuture;
                }
            }
        }

        internal class LivenessInfo {
            public Chunk          Chunk;
            public int?           Count;
            public int            DeadFrameCount;
        }

        internal class ChunkInitializer<TElement>
            where TElement : struct {
            public ParticleSystem System;
            public int Remaining;
            public BufferInitializer<TElement> Position, Velocity, Color;
            public Chunk Chunk;
            public bool HasFailed;

            public void Run (ThreadGroup g) {
                if (g != null) {
                    var q = g.GetQueueForType<BufferInitializer<TElement>>();
                    Position.Parent = Velocity.Parent = Color.Parent = this;

                    q.Enqueue(ref Position);
                    q.Enqueue(ref Velocity);
                    if (Color.Initializer != null)
                        q.Enqueue(ref Color);
                } else {
                    Position.Execute();
                    Velocity.Execute();
                    if (Color.Initializer != null)
                        Color.Execute();
                }
            }

            public void OnBufferInitialized (bool failed) {
                var result = Interlocked.Decrement(ref Remaining);
                if (failed)
                    HasFailed = true;

                if (result == 0) {
                    if (!failed)
                    lock (System.NewUserChunks)
                        System.NewUserChunks.Add(Chunk);
                }
            }
        }

#if FNA
        // FIXME: We should be able to separate out the upload operation, I think?
        internal struct BufferInitializer<TElement> : IMainThreadWorkItem
#else
        internal struct BufferInitializer<TElement> : IWorkItem
#endif
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

            public int  LastFrameDependency = -1;
            public long LastTurnUsed;

            public RenderTargetBinding[] Bindings2, Bindings3, Bindings4;
            public RenderTarget2D PositionAndLife;
            public RenderTarget2D Velocity;

            public bool IsDisposed { get; private set; }

            private static volatile int NextID;

            public BufferSet (ParticleEngineConfiguration configuration, GraphicsDevice device) {
                ID = Interlocked.Increment(ref NextID);
                Size = configuration.ChunkSize;
                MaximumCount = Size * Size;

                Bindings2 = new RenderTargetBinding[2];
                Bindings3 = new RenderTargetBinding[3];
                Bindings4 = new RenderTargetBinding[4];

                PositionAndLife = CreateRenderTarget(configuration, device);
                Velocity = CreateRenderTarget(configuration, device);

                Bindings4[0] = Bindings3[0] = Bindings2[0] = new RenderTargetBinding(PositionAndLife);
                Bindings4[1] = Bindings3[1] = Bindings2[1] = new RenderTargetBinding(Velocity);
            }

            internal RenderTarget2D CreateRenderTarget (ParticleEngineConfiguration configuration, GraphicsDevice device) {
                return new RenderTarget2D(
                    device, 
                    Size, Size, false, 
                    SurfaceFormat.Vector4, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                IsDisposed = true;
                PositionAndLife.Dispose();
                Velocity.Dispose();
            }
        }

        public class Chunk : IDisposable {
            public readonly ParticleSystem System;

            public long GlobalIndexOffset;
            public readonly int Size, MaximumCount;
            public int ID;
            public int RefCount;

            internal BufferSet Previous, Current;

            public RenderTarget2D Color;

            internal RenderTarget2D RenderData, RenderColor;

            public bool NoLongerASpawnTarget { get; internal set; }
            public bool IsFeedbackSource { get; internal set; }
            public bool IsDisposed { get; private set; }

            public int TotalSpawned { get; internal set; }
            public int TotalConsumedForFeedback { get; internal set; }
            public int NextSpawnOffset { get; internal set; }
            public int AvailableForFeedback {
                get {
                    return TotalSpawned - TotalConsumedForFeedback;
                }
            }
            public int FeedbackSourceIndex {
                get {
                    return TotalConsumedForFeedback;
                }
            }
            public int Free {
                get {
                    return MaximumCount - NextSpawnOffset;
                }
            }

            private static volatile int NextID;

            public Chunk (
                ParticleSystem system
            ) {
                System = system;
                var configuration = system.Engine.Configuration;
                ID = Interlocked.Increment(ref NextID);
                Size = configuration.ChunkSize;
                MaximumCount = Size * Size;

                var device = system.Engine.Coordinator.Device;
                Color = MakeRT(device);
                RenderData = MakeRT(device);
                RenderColor = MakeRT(device);
            }

            private RenderTarget2D MakeRT (GraphicsDevice device) {
                return new RenderTarget2D(
                    device, Size, Size, false, SurfaceFormat.Vector4,
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                );
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                IsDisposed = true;

                Color.Dispose();
                RenderData.Dispose();
                RenderColor.Dispose();
            }

            internal void Clear () {
                NextSpawnOffset = 0;
                TotalConsumedForFeedback = 0;
                TotalSpawned = 0;
                IsFeedbackSource = false;
                NoLongerASpawnTarget = true;
            }

            internal void SkipFeedbackInput (int skipAmount) {
                TotalConsumedForFeedback += skipAmount;
                if (TotalConsumedForFeedback > TotalSpawned)
                    TotalConsumedForFeedback = TotalSpawned;
            }
        }

        private class RenderHandler {
            public readonly ParticleSystem System;
            public readonly Action<DeviceManager, object> BeforeDraw, AfterDraw;

            public RenderHandler (ParticleSystem system) {
                System = system;
                BeforeDraw = _BeforeDraw;
                AfterDraw = _AfterDraw;
            }

            private void _BeforeDraw (DeviceManager dm, object _rp) {
                var rp = (InternalRenderParameters)_rp;
                var m = rp.Material;
                var e = m.Effect;
                var p = e.Parameters;

                // FIXME: deltaTime
                System.SetSystemUniforms(m, 0);
                var appearance = System.Configuration.Appearance;

                var tex = appearance?.Texture?.Instance;
                if ((tex != null) && tex.IsDisposed)
                    tex = null;
                var texSize = (tex != null)
                    ? new Vector2(tex.Width, tex.Height)
                    : Vector2.One;

                // TODO: transform arg
                var bt = p["BitmapTexture"];
                if (bt != null) {
                    bt.SetValue(tex);
                    if (tex != null) {
                        // var offset = new Vector2(-0.5f) / texSize;
                        var offset = appearance.OffsetPx / texSize;
                        var size = appearance.SizePx.GetValueOrDefault(texSize) / texSize;
                        p["BitmapTextureRegion"]?.SetValue(new Vector4(
                            offset.X, offset.Y, 
                            offset.X + size.X, offset.Y + size.Y
                        ));
                    }
                }
                
                p["StippleFactor"]?.SetValue(rp.UserParameters?.StippleFactor ?? System.Configuration.StippleFactor);

                var origin = rp.UserParameters?.Origin ?? Vector2.Zero;
                var scale = rp.UserParameters?.Scale ?? Vector2.One;

                var u = new RasterizeParticleSystem(System.Engine.Configuration, System.Configuration, origin, scale);
                System.Engine.uRasterize.TrySet(m, ref u);

                p["ColumnFromVelocity"]?.SetValue((appearance?.ColumnFromVelocity ?? false) ? 1f : 0f);
                p["RowFromVelocity"]?.SetValue((appearance?.RowFromVelocity ?? false) ? 1f : 0f);
                p["BitmapBilinear"]?.SetValue((appearance?.Bilinear ?? true) ? 1f : 0f);
                p["Rounded"]?.SetValue((appearance?.Rounded ?? false) ? 1f : 0f);

                System.MaybeSetLifeRampParameters(p);
            }

            private void _AfterDraw (DeviceManager dm, object _rp) {
                var rp = (InternalRenderParameters)_rp;
                var m = rp.Material;
                var e = m.Effect;
                var p = e.Parameters;

                p.ClearTextures(ClearTextureList);
                // ughhhhhhhhhh
#if !FNA
                for (var i = 0; i < 4; i++)
                    dm.Device.VertexTextures[i] = null;
                for (var i = 0; i < 16; i++)
                    dm.Device.Textures[i] = null;
#endif
            }
        }

        internal static readonly string[] ClearTextureList = new[] {
            "PositionTexture", "VelocityTexture", "AttributeTexture",
            "LifeRampTexture", "BitmapTexture", "RampTexture",
            "RandomnessTexture", "LowPrecisionRandomnessTexture",
            "PatternTexture", "DistanceFieldTexture"
        };

        public bool IsDisposed { get; private set; }
        public int LiveCount { get; private set; }

        public readonly ParticleEngine                     Engine;
        public readonly ParticleSystemConfiguration        Configuration;
        public readonly List<Transforms.ParticleTransform> Transforms = 
            new List<Transforms.ParticleTransform>();

        private object ReadbackLock = new object();
        private float  ReadbackTimestamp;
        private Future<ArraySegment<BitmapDrawCall>> ReadbackFuture = new Future<ArraySegment<BitmapDrawCall>>();
        private BitmapDrawCall[] ReadbackResultBuffer;
        private Vector4[] ReadbackBuffer1, ReadbackBuffer2, ReadbackBuffer3;

        private readonly Transforms.ParticleTransform.UpdateHandler Updater;
        private readonly RenderHandler Renderer;

        private  readonly List<Chunk> NewUserChunks = new List<Chunk>();
        internal readonly List<Chunk> Chunks = new List<Chunk>();

        private readonly Dictionary<int, LivenessInfo> LivenessInfos = new Dictionary<int, LivenessInfo>();

        private int CurrentSpawnTarget;
        private int CurrentFeedbackSpawnTarget;
        private int CurrentFeedbackSource;

        private int CurrentFrameIndex;

        internal long LastClearTimestamp;
        internal bool IsClearPending;

        private int LastResetCount = 0;
        private int LastFrameUpdated = -1;
        public event Action<ParticleSystem> OnDeviceReset;

        // HACK: Performing occlusion queries every frame seems to be super unreliable,
        //  so just perform them intermittently and accept that our data will be outdated
        public const int LivenessCheckInterval = 4;
        private int FramesUntilNextLivenessCheck = LivenessCheckInterval;

        private double? LastUpdateTimeSeconds = null;
        private double  UpdateErrorAccumulator = 0;

        private readonly AutoRenderTarget LivenessQueryRT;

        public long TotalSpawnCount { get; private set; }

        /// <summary>
        /// The number of frames a chunk must be dead for before it is reclaimed
        /// </summary>
        public int DeadFrameThreshold = LivenessCheckInterval * 3;

        public ParticleSystem (
            ParticleEngine engine, ParticleSystemConfiguration configuration
        ) {
            if (engine == null)
                throw new ArgumentNullException("engine");

            Engine = engine;
            Configuration = configuration;
            LiveCount = 0;
            Renderer = new RenderHandler(this);
            Updater = new Transforms.ParticleTransform.UpdateHandler(null);

            engine.Systems.Add(this);

            LivenessQueryRT = new AutoRenderTarget(engine.Coordinator, MaxChunkCount, 1, false, SurfaceFormat.Rg32, DepthFormat.Depth24, 1);
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

        private Chunk ChunkFromID (int id) {
            foreach (var c in Chunks)
                if (c.ID == id)
                    return c;

            return null;
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
        
        private Chunk CreateChunk () {
            if (Chunks.Count >= MaxChunkCount)
                throw new Exception("Hit maximum chunk count");

            lock (Engine.Coordinator.CreateResourceLock) {
                var result = new Chunk(this);
                result.Current = AcquireOrCreateBufferSet();
                result.GlobalIndexOffset = TotalSpawnCount;
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
            Action<TElement[], int> colorInitializer
        ) where TElement : struct {
            var mc = ChunkMaximumCount;
            int numToSpawn = (int)Math.Ceiling((double)particleCount / mc);

            var g = parallel ? Engine.Coordinator.ThreadGroup : null;

            for (int i = 0; i < numToSpawn; i++) {
                var c = CreateChunk();
                // Console.WriteLine("Creating new chunk " + c.ID);
                var offset = i * mc;
                var curr = c.Current;
                var pos = new BufferInitializer<TElement> { Buffer = curr.PositionAndLife, Initializer = positionInitializer, Offset = offset };
                var vel = new BufferInitializer<TElement> { Buffer = curr.Velocity, Initializer = velocityInitializer, Offset = offset };
                var attr = new BufferInitializer<TElement> { Buffer = c.Color, Initializer = colorInitializer, Offset = offset };
                var job = new ChunkInitializer<TElement> {
                    System = this,
                    Position = pos,
                    Velocity = vel,
                    Color = attr,
                    Chunk = c,
                    Remaining = (colorInitializer != null) ? 3 : 2
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

        int IParticleSystems.SystemCount {
            get {
                return 1;
            }
        }

        ParticleSystem IParticleSystems.this[int index] {
            get {
                if (index != 0)
                    throw new ArgumentOutOfRangeException("index");
                return this;
            }
        }

        internal Chunk PickTargetForSpawn (
            bool feedback, int count, 
            ref int currentTarget, out bool needClear
        ) {
            var chunk = ChunkFromID(currentTarget);
            // FIXME: Ideally we could split the spawn across this chunk and an old one.
            if (chunk != null) {
                if (chunk.Free < 16) {
                    chunk.NoLongerASpawnTarget = true;
                    currentTarget = -1;
                    chunk = null;
                }
            }

            if (chunk == null) {
                chunk = CreateChunk();
                chunk.IsFeedbackSource = feedback;
                currentTarget = chunk.ID;
                Chunks.Add(chunk);
                needClear = true;
            } else {
                needClear = false;
            }

            return chunk;
        }

        internal Chunk GetCurrentSpawnTarget (bool feedback) {
            return ChunkFromID(feedback ? CurrentFeedbackSpawnTarget : CurrentSpawnTarget);
        }

        internal Chunk PickTargetForSpawn (bool feedback, int count, out bool needClear) {
            if (feedback)
                return PickTargetForSpawn(true, count, ref CurrentFeedbackSpawnTarget, out needClear);
            else
                return PickTargetForSpawn(false, count, ref CurrentSpawnTarget, out needClear);
        }

        internal Chunk PickSourceForFeedback (int count) {
            var cfs = ChunkFromID(CurrentFeedbackSource);
            if (cfs != null) {
                if ((cfs.AvailableForFeedback <= 0) && (cfs.Free <= 0))
                    cfs = null;
            }
            var newChunk = Chunks.FirstOrDefault(
                c => (c.AvailableForFeedback >= count / 2) && !c.IsFeedbackSource
            );
            if (newChunk != null)
                CurrentFeedbackSource = newChunk.ID;
            return newChunk;
        }

        private bool RunSpawner (
            IBatchContainer container, ref int layer,
            long startedWhen, Transforms.SpawnerBase spawner,
            double deltaTimeSeconds, double now, bool isSecondPass
        ) {
            int spawnCount = 0, requestedSpawnCount;

            if (!spawner.IsValid)
                return false;

            Chunk sourceChunk;
            spawner.BeginTick(this, now, deltaTimeSeconds, out requestedSpawnCount, out sourceChunk);

            if (requestedSpawnCount <= 0) {
                return false;
            } else if (requestedSpawnCount > ChunkMaximumCount)
                spawnCount = ChunkMaximumCount;
            else
                spawnCount = requestedSpawnCount;

            bool needClear;
            var fs = spawner as Transforms.FeedbackSpawner;
            var chunk = PickTargetForSpawn(fs != null, spawnCount, out needClear);

            if (spawnCount > chunk.Free)
                spawnCount = chunk.Free;

            if (chunk == null)
                throw new Exception("Failed to locate or create a chunk to spawn in");

            var first = chunk.NextSpawnOffset;
            var last = chunk.NextSpawnOffset + spawnCount - 1;
            spawner.SetIndices(first, last);

            chunk.NextSpawnOffset += spawnCount;
            TotalSpawnCount += spawnCount;
            if (sourceChunk != null) {
                var consumedCount = spawnCount;
                // HACK
                if ((fs != null) && !fs.SpawnFromEntireWindow) {
                    consumedCount = Math.Max(consumedCount / fs.InstanceMultiplier, 1);
                    sourceChunk.TotalConsumedForFeedback += consumedCount;
                }
            }

            // Console.WriteLine("Spawning {0} into {1} (w/{2} free)", spawnCount, chunk.ID, chunk.Free);
            spawner.EndTick(requestedSpawnCount, spawnCount);
            chunk.TotalSpawned += spawnCount;

            if (spawnCount > 0) {
                var h = isSecondPass ? spawner.Handler2 : spawner.Handler;

                RunTransform(
                    chunk, container, ref layer, ((Transforms.IParticleTransform)spawner).GetMaterial(Engine.ParticleMaterials),
                    startedWhen, true, false,
                    h.BeforeDraw, h.AfterDraw, 
                    deltaTimeSeconds, needClear, now, false,
                    sourceChunk
                );
            }

            var isPartialSpawn = (requestedSpawnCount > spawnCount);
            return isPartialSpawn;
        }

        private void RunTransform (
            Chunk chunk, IBatchContainer container, ref int layer, Material m,
            long startedWhen, bool isSpawning, bool isAnalyzer,
            Action<DeviceManager, object> beforeDraw,
            Action<DeviceManager, object> afterDraw,
            double deltaTimeSeconds, bool shouldClear,
            double now, bool isUpdate, Chunk sourceChunk
        ) {
            if (chunk == null)
                throw new ArgumentNullException();

            var device = container.RenderManager.DeviceManager.Device;

            var e = m.Effect;
            var p = (e != null) ? e.Parameters : null;

            var li = GetLivenessInfo(chunk);

            var chunkMaterial = m;
            if (isSpawning)
                li.DeadFrameCount = 0;
            else if (!isAnalyzer)
                RotateBuffers(chunk, container.RenderManager.DeviceManager.FrameIndex);

            var prev = chunk.Previous;
            var curr = chunk.Current;

            if (prev != null)
                prev.LastTurnUsed = Engine.CurrentTurn;
            curr.LastTurnUsed = Engine.CurrentTurn;

            if (e != null)
                RenderTrace.Marker(container, layer++, "System {0:X8} Transform {1} Chunk {2}", GetHashCode(), e.CurrentTechnique.Name, chunk.ID);

            var up = new Transforms.ParticleTransformUpdateParameters {
                System = this,
                Material = m,
                Prev = prev,
                Curr = curr,
                IsUpdate = isUpdate,
                IsSpawning = isSpawning,
                ShouldClear = shouldClear,
                Chunk = chunk,
                SourceChunk = sourceChunk,
                SourceData = sourceChunk?.Previous ?? sourceChunk?.Current,
                Now = (float)now,
                DeltaTimeSeconds = deltaTimeSeconds,
                CurrentFrameIndex = CurrentFrameIndex
            };

            using (var batch = NativeBatch.New(
                container, layer++, m,
                beforeDraw,
                afterDraw, up
            ))  {
                if (e != null)
                    batch.Add(new NativeDrawCall(
                        PrimitiveType.TriangleList, Engine.TriVertexBuffer, 0,
                        Engine.TriIndexBuffer, 0, 0, Engine.TriVertexBuffer.VertexCount, 0, Engine.TriVertexBuffer.VertexCount / 2
                    ));
            }

            if (isUpdate) {
                curr.LastFrameDependency = container.RenderManager.DeviceManager.FrameIndex;
                // Console.WriteLine("Updating into {0}", curr.ID);
            } else {
                // Console.WriteLine("Transforming through {0}", curr.ID);
            }
        }

        public void Reset () {
            LastClearTimestamp = Time.Ticks;
            IsClearPending = true;
            TotalSpawnCount = 0;
            UpdateErrorAccumulator = 0;
            CurrentFrameIndex = 0;
            LastUpdateTimeSeconds = null;
            foreach (var xform in Transforms)
                xform.Reset();
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
            Action<Vector4[], int> colorInitializer,
            bool parallel = true
        ) {
            var result = InitializeNewChunks(
                particleCount,
                Engine.Coordinator.Device,
                parallel,
                positionInitializer,
                velocityInitializer,
                colorInitializer
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
            Action<HalfVector4[], int> colorInitializer,
            bool parallel = true
        ) {
            var result = InitializeNewChunks(
                particleCount,
                Engine.Coordinator.Device,
                parallel,
                positionInitializer,
                velocityInitializer,
                colorInitializer
            );
            return result;
        }

        internal HashSet<LivenessInfo> ChunksToReap = new HashSet<LivenessInfo>();

        private void UpdateLivenessAndReapDeadChunks () {
            // FIXME
            return;

            LiveCount = 0;

            foreach (var kvp in LivenessInfos) {
                var isDead = false;
                var li = kvp.Value;
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
                LivenessInfos.Remove(li.Chunk.ID);
                Reap(li.Chunk);
            }

            ChunksToReap.Clear();
        }

        private void Reap (BufferSet buffer) {
            if (buffer == null)
                return;
            if (buffer.Size != Engine.Configuration.ChunkSize)
                Engine.Coordinator.DisposeResource(buffer);
            else
                Engine.DiscardedBuffers.Add(buffer);
        }

        private void Reap (Chunk chunk) {
            // Console.WriteLine("Chunk reaped");
            Reap(chunk.Previous);
            Reap(chunk.Current);
            chunk.Previous = chunk.Current = null;
            Chunks.Remove(chunk);
            chunk.Clear();
            Engine.Coordinator.DisposeResource(chunk);
        }

        internal void SetSystemUniforms (Material m, double deltaTimeSeconds) {
            ClampedBezier4 colorFromLife, colorFromVelocity;
            ClampedBezier1 sizeFromLife, sizeFromVelocity, roundingFromLife;

            var psu = new Uniforms.ParticleSystem(Engine.Configuration, Configuration, deltaTimeSeconds);
            Engine.uSystem.Set(m, ref psu);

            var o = Configuration.Color._OpacityFromLife.GetValueOrDefault(0);
            if (o != 0) {
                colorFromLife = new ClampedBezier4 {
                    A = new Vector4(1, 1, 1, 0),
                    B = Vector4.One,
                    RangeAndCount = new Vector4(0, 1.0f / o, 2, 0)
                };
            } else {
                colorFromLife = new ClampedBezier4(Configuration.Color._ColorFromLife);
            }
            colorFromVelocity = new ClampedBezier4(Configuration.Color.ColorFromVelocity);

            sizeFromLife = new ClampedBezier1(Configuration.SizeFromLife);
            sizeFromVelocity = new ClampedBezier1(Configuration.SizeFromVelocity);
            roundingFromLife = new ClampedBezier1(Configuration.Appearance.RoundingPowerFromLife);

            Engine.uColorFromLife.TrySet(m, ref colorFromLife);
            Engine.uSizeFromLife.TrySet(m, ref sizeFromLife);
            Engine.uRoundingPowerFromLife.TrySet(m, ref roundingFromLife);
            Engine.uColorFromVelocity.TrySet(m, ref colorFromVelocity);
            Engine.uSizeFromVelocity.TrySet(m, ref sizeFromVelocity);
        }

        private BufferSet AcquireOrCreateBufferSet () {
            BufferSet result;
            if (!Engine.AvailableBuffers.TryPopFront(out result))
                result = CreateBufferSet(Engine.Coordinator.Device);
            result.LastTurnUsed = Engine.CurrentTurn;
            return result;
        }

        private void RotateBuffers (Chunk chunk, int frameIndex) {
            Engine.NextTurn(frameIndex);

            var prev = chunk.Previous;
            chunk.Previous = chunk.Current;
            chunk.Current = AcquireOrCreateBufferSet();
            if (prev != null)
                Engine.DiscardedBuffers.Add(prev);
        }

        public UpdateResult Update (IBatchContainer container, int layer) {
            var lastUpdateTimeSeconds = LastUpdateTimeSeconds;
            var updateError = UpdateErrorAccumulator;
            UpdateErrorAccumulator = 0;
            var now = TimeProvider.Seconds;
            CurrentFrameIndex++;

            if (LastFrameUpdated >= container.RenderManager.DeviceManager.FrameIndex)
                throw new InvalidOperationException("Cannot update twice in a single frame");

            var ups = Engine.Configuration.UpdatesPerSecond;
            var maxDeltaTime = Arithmetic.Clamp(Engine.Configuration.MaximumUpdateDeltaTimeSeconds, 1 / 200f, 10f);

            var tickUnit = 1.0 / Arithmetic.Clamp(ups.GetValueOrDefault(60), 5, 200);
            var actualDeltaTimeSeconds = tickUnit;
            if (lastUpdateTimeSeconds.HasValue)
                actualDeltaTimeSeconds = Math.Min(now - lastUpdateTimeSeconds.Value, maxDeltaTime);

            if (ups.HasValue && lastUpdateTimeSeconds.HasValue) {
                actualDeltaTimeSeconds += updateError;
                var tickCount = (int)Math.Floor(actualDeltaTimeSeconds / tickUnit);
                if (tickCount < 0)
                    tickCount = 0;
                var adjustedDeltaTime = tickCount * tickUnit;
                UpdateErrorAccumulator = actualDeltaTimeSeconds - adjustedDeltaTime;
                actualDeltaTimeSeconds = adjustedDeltaTime;
                if ((actualDeltaTimeSeconds <= 0) && (CurrentFrameIndex > 1))
                    return new UpdateResult(this, false, (float)now);
                LastUpdateTimeSeconds = now = lastUpdateTimeSeconds.Value + adjustedDeltaTime;
            } else {
                LastUpdateTimeSeconds = now;
            }

            LastFrameUpdated = container.RenderManager.DeviceManager.FrameIndex;

            actualDeltaTimeSeconds = Math.Min(actualDeltaTimeSeconds, maxDeltaTime);

            // Console.WriteLine(actualDeltaTimeSeconds);

            var startedWhen = Time.Ticks;

            if (LastResetCount != Engine.ResetCount) {
                if (OnDeviceReset != null)
                    OnDeviceReset(this);
                LastResetCount = Engine.ResetCount;
            }

            var initialTurn = Engine.CurrentTurn;

            var pm = Engine.ParticleMaterials;

            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    dm.PushRenderTarget(null);
                    lock (ReadbackLock)
                    if (Configuration.AutoReadback)
                        ReadbackFuture = new Future<ArraySegment<BitmapDrawCall>>();
                },
                (dm, _) => {
                    dm.PopRenderTarget();
                    MaybePerformReadback((float)now);
                }
            )) {
                int i = 0;

                lock (NewUserChunks) {
                    foreach (var nc in NewUserChunks) {
                        nc.GlobalIndexOffset = TotalSpawnCount;
                        nc.NoLongerASpawnTarget = true;
                        TotalSpawnCount += nc.MaximumCount;
                        Chunks.Add(nc);
                    }

                    NewUserChunks.Clear();
                }

                if (IsClearPending) {
                    foreach (var c in Chunks.ToList()) {
                        if (c.Size == Engine.Configuration.ChunkSize)
                            c.Clear();
                        Reap(c);
                    }
                    IsClearPending = false;
                    TotalSpawnCount = 0;
                    LivenessInfos.Clear();
                }

                bool computingLiveness = false;
                if (FramesUntilNextLivenessCheck-- <= 0) {
                    FramesUntilNextLivenessCheck = LivenessCheckInterval;
                    computingLiveness = true;
                }

                foreach (var t in Transforms)
                    t.BeforeFrame(Engine);

                foreach (var s in Transforms.OfType<Transforms.SpawnerBase>()) {
                    if (!s.IsActive)
                        continue;

                    var it = (Transforms.IParticleTransform)s;
                    var isPartialSpawn = RunSpawner(
                        group, ref i, startedWhen, s,
                        actualDeltaTimeSeconds, now, false
                    );
                    if (isPartialSpawn)
                        RunSpawner(
                            group, ref i, startedWhen, s,
                            actualDeltaTimeSeconds, now, true
                        );
                }

                foreach (var chunk in Chunks)
                    UpdateChunk(chunk, now, (float)actualDeltaTimeSeconds, startedWhen, pm, group, ref i, computingLiveness);

                // FIXME: This here, this thing, randomly adds 10-20ms to BeginDraw making us miss vsync. Sick. Awesome.
                if (computingLiveness)
                    ComputeLiveness(group, i++);

                if (computingLiveness) {
                    lock (LivenessInfos)
                        UpdateLivenessAndReapDeadChunks();
                }
            }

            var ts = Time.Ticks;

            var cbk = (SendOrPostCallback)((_) => {
                foreach (var t in Transforms)
                    t.AfterFrame(Engine);
            });

            var sc = SynchronizationContext.Current;
            Engine.Coordinator.AfterPresent(() => {
                if (sc != null)
                    sc.Post(cbk, null);
                else
                    cbk(null);
            });

            Engine.EndOfUpdate(initialTurn, container.RenderManager.DeviceManager.FrameIndex);
            return new UpdateResult(this, true, (float)now);
        }

        private void UpdateChunk (
            Chunk chunk, double now, 
            float actualDeltaTimeSeconds, long startedWhen, 
            ParticleMaterials pm, BatchGroup group, 
            ref int i, bool computingLiveness
        ) {
            var isFirstXform = true;
            foreach (var t in Transforms) {
                var it = (Transforms.IParticleTransform)t;

                var shouldSkip = !t.IsActive || (t is Transforms.SpawnerBase) || !t.IsValid;
                if (shouldSkip)
                    continue;

                RunTransform(
                    chunk, group, ref i, it.GetMaterial(Engine.ParticleMaterials),
                    startedWhen, false, it.IsAnalyzer, it.BeforeDraw, it.AfterDraw,
                    actualDeltaTimeSeconds, isFirstXform && !it.IsAnalyzer, now, false, null
                );

                if (!it.IsAnalyzer)
                    isFirstXform = false;
            }

            if (IsClearPending) {
                // occlusion queries suck and never work right, and for some reason
                //  the old particle data is a ghost from hell and refuses to disappear
                //  even after it is cleared
                for (int k = 0; k < 2; k++) {
                    RunTransform(
                        chunk, group, ref i, pm.Erase,
                        startedWhen, false, false,
                        null, null, actualDeltaTimeSeconds, 
                        true, now, true, null
                    );
                }

                // FIXME: Still fucked
            } else if (Configuration.Collision?.DistanceField != null) {
                if (Configuration.Collision.DistanceFieldMaximumZ == null)
                    throw new InvalidOperationException("If a distance field is active, you must set DistanceFieldMaximumZ");

                RunTransform(
                    chunk, group, ref i, pm.UpdateWithDistanceField,
                    startedWhen, false, false,
                    Updater.BeforeDraw, Updater.AfterDraw, 
                    actualDeltaTimeSeconds, true, now, true, null
                );
            } else {
                RunTransform(
                    chunk, group, ref i, pm.UpdatePositions,
                    startedWhen, false, false, 
                    Updater.BeforeDraw, Updater.AfterDraw,
                    actualDeltaTimeSeconds, true, now, true, null
                );
            }
        }

        private void CopyBuffer (IBatchContainer container, ref int layer, RenderTarget2D from, RenderTarget2D to) {
            using (var group = BatchGroup.ForRenderTarget(container, ++layer, to))
            using (var bb = BitmapBatch.New(group, 0, Engine.Materials.GetBitmapMaterial(false, RasterizerState.CullNone, DepthStencilState.None, BlendState.Opaque), samplerState: SamplerState.PointClamp))
                bb.Add(new BitmapDrawCall(from, Vector2.Zero));
        }

        private void AutoGrowBuffer<T> (ref T[] buffer, int size) {
            if ((buffer == null) || (buffer.Length < size))
                buffer = new T[size];
        }

        private void MaybePerformReadback (float timestamp) {
            Future<ArraySegment<BitmapDrawCall>> f;

            lock (ReadbackLock) {
                if (!Configuration.AutoReadback)
                    return;

                f = ReadbackFuture;
                if (f == null)
                    return;
            }

            ReadbackTimestamp = timestamp;
            var chunkCount = Engine.Configuration.ChunkSize * Engine.Configuration.ChunkSize;
            var maxTotalCount = Chunks.Count * chunkCount;
            var bufferSize = (int)Math.Ceiling(maxTotalCount / 4096.0) * 4096;
            AutoGrowBuffer(ref ReadbackBuffer1, chunkCount);
            AutoGrowBuffer(ref ReadbackBuffer2, chunkCount);
            AutoGrowBuffer(ref ReadbackBuffer3, chunkCount);
            AutoGrowBuffer(ref ReadbackResultBuffer, maxTotalCount);

            // FIXME: This is too slow
            // Array.Clear(ReadbackResultBuffer, 0, ReadbackResultBuffer.Length);

            Configuration.Appearance?.Texture?.EnsureInitialized(Engine.Configuration.TextureLoader);

            int totalCount = 0;
            // FIXME: Do this in parallel
            foreach (var c in Chunks) {
                var curr = c.Current;
                if (curr.IsDisposed)
                    continue;
                var rowCount = (int)Math.Ceiling(c.TotalSpawned / (float)Engine.Configuration.ChunkSize);
                var eleCount = rowCount * Engine.Configuration.ChunkSize;
                var rect = new Rectangle(0, 0, Engine.Configuration.ChunkSize, rowCount);
                curr.PositionAndLife.GetData(0, rect, ReadbackBuffer1, 0, eleCount);
                c.RenderData.GetData(0, rect, ReadbackBuffer2, 0, eleCount);
                c.RenderColor.GetData(0, rect, ReadbackBuffer3, 0, eleCount);
                totalCount += FillReadbackResult(
                    ReadbackResultBuffer, ReadbackBuffer1, ReadbackBuffer2, ReadbackBuffer3,
                    totalCount, eleCount, ReadbackTimestamp
                );
            }

            f.SetResult(new ArraySegment<BitmapDrawCall>(
                ReadbackResultBuffer, 0, totalCount
            ), null);
        }

        private int FillReadbackResult (
            BitmapDrawCall[] buffer, Vector4[] positionAndLife, Vector4[] renderData, Vector4[] renderColor,
            int offset, int count, float now
        ) {
            // var sfl = new ClampedBezier1(Configuration.SizeFromLife);

            Vector2 pSize;
            BitmapDrawCall dc = default(BitmapDrawCall);
            dc.Texture = Configuration.Appearance?.Texture?.Instance;
            dc.Origin = Vector2.One * 0.5f;

            var animRate = Configuration.Appearance?.AnimationRate ?? Vector2.Zero;
            var animRateAbs = new Vector2(Math.Abs(animRate.X), Math.Abs(animRate.Y));
            var cfv = Configuration.Appearance?.ColumnFromVelocity ?? false;
            var rfv = Configuration.Appearance?.RowFromVelocity ?? false;
            var c = Color.White;

            var region = Bounds.Unit;

            if (dc.Texture != null) {
                var sizeF = new Vector2(dc.Texture.Width, dc.Texture.Height);
                dc.TextureRegion = region = Bounds.FromPositionAndSize(
                    Configuration.Appearance.OffsetPx / sizeF,
                    Configuration.Appearance.SizePx.GetValueOrDefault(sizeF) / sizeF
                );
                if (Configuration.Appearance.RelativeSize)
                    pSize = Configuration.Size;
                else
                    pSize = Configuration.Size / sizeF;
            } else {
                pSize = Configuration.Size;
            }

            var texSize = region.Size;
            var frameCountX = Math.Max((int)(1.0f / texSize.X), 1);
            var frameCountY = Math.Max((int)(1.0f / texSize.Y), 1);
            var maxAngleX = (2 * Math.PI) / frameCountX;
            var maxAngleY = (2 * Math.PI) / frameCountY;
            var velRotation = Configuration.RotationFromVelocity ? 1.0 : 0.0f;

            var sr = Configuration.SortedReadback;
            var zToY = Configuration.ZToY;

            int result = 0;
            for (int i = 0, l = count; i < l; i++) {
                var pAndL = positionAndLife[i];
                var life = pAndL.W;
                if (life <= 0)
                    continue;

                var rd = renderData[i];
                var rc = renderColor[i];

                var sz = rd.X;
                var rot = rd.Y % (float)(2 * Math.PI);

                if ((frameCountX > 1) || (frameCountY > 1)) {
                    var frameIndexXy = (animRateAbs * life).Floor();

                    frameIndexXy.Y += (float)Math.Floor(rd.W);
                    if (cfv)
                        frameIndexXy.X += (float)Math.Round(rot / maxAngleX);
                    if (rfv)
                        frameIndexXy.Y += (float)Math.Round(rot / maxAngleY);

                    frameIndexXy.X = Math.Max(0, frameIndexXy.X) % frameCountX;
                    frameIndexXy.Y = Arithmetic.Clamp(frameIndexXy.Y, 0, frameCountY - 1);
                    if (animRate.X < 0)
                        frameIndexXy.X = frameCountX - frameIndexXy.X;
                    if (animRate.Y < 0)
                        frameIndexXy.Y = frameCountY - frameIndexXy.Y;
                    var texOffset = frameIndexXy * texSize;

                    dc.TextureRegion = region;
                    dc.TextureRegion.TopLeft += texOffset;
                    dc.TextureRegion.BottomRight += texOffset;
                }

                dc.Position = new Vector2(pAndL.X, pAndL.Y);
                if (sr)
                    dc.SortKey.Order = pAndL.Y + zToY;
                dc.Scale = pSize * sz;
                c.R = (byte)(rc.X * 255);
                c.G = (byte)(rc.Y * 255);
                c.B = (byte)(rc.Z * 255);
                c.A = (byte)(rc.W * 255);
                dc.MultiplyColor = c;
                dc.Rotation = (float)(velRotation * rot);

                buffer[result + offset] = dc;
                result++;
            }

            return result;
        }

        private void ReadLivenessDataFromRT () {
            var buffer = new Rg32[MaxChunkCount];
            lock (Engine.Coordinator.UseResourceLock)
                LivenessQueryRT?.Get()?.GetData(buffer);

            lock (LivenessInfos)
            for (int i = 0; i < Chunks.Count; i++) {
                var chunk = Chunks[i];
                var li = GetLivenessInfo(chunk);
                if (li == null) 
                    continue;

                li.Count = (int)(buffer[i].PackedValue);
            }
        }

        private void ComputeLiveness (
            BatchGroup group, int layer
        ) {
#if FNA
            return;
#endif
            var quadCount = ChunkMaximumCount;

            Engine.Coordinator.BeforePrepare(ReadLivenessDataFromRT);

            using (var rtg = BatchGroup.ForRenderTarget(
                group, layer, LivenessQueryRT, (dm, _) => {
                    dm.Device.Clear(Color.Black);
                }
            )) {
                RenderTrace.Marker(rtg, -9999, "Compute chunk liveness");

                var m = Engine.ParticleMaterials.CountLiveParticles;

                for (int i = 0; i < Chunks.Count; i++) {
                    var chunk = Chunks[i];
                    var li = GetLivenessInfo(chunk);
                    if (li == null)
                        continue;

                    using (var chunkBatch = NativeBatch.New(
                        rtg, chunk.ID, m, (dm, _) => {
                            SetSystemUniforms(m, 0);
                            var p = m.Effect.Parameters;
                            p["ChunkIndexAndMaxIndex"].SetValue(new Vector2(i, LivenessQueryRT.Width));
                            p["PositionTexture"].SetValue(chunk.Current.PositionAndLife);
                            m.Flush();
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
            BatchGroup group, Chunk chunk, Material m, int layer, bool usePreviousData
        ) {
            // TODO: Actual occupied count?
            var quadCount = ChunkMaximumCount;

            var src = usePreviousData ? chunk.Previous : chunk.Current;

            // Console.WriteLine("Draw {0}", curr.ID);

            using (var batch = NativeBatch.New(
                group, layer, m, (dm, _) => {
                    var p = m.Effect.Parameters;
                    p["PositionTexture"].SetValue(src.PositionAndLife);
                    // HACK
                    p["VelocityTexture"].SetValue(chunk.RenderData);
                    p["AttributeTexture"].SetValue(chunk.RenderColor);
                    m.Flush();
                    p["PositionTexture"].SetValue(src.PositionAndLife);
                    // HACK
                    p["VelocityTexture"].SetValue(chunk.RenderData);
                    p["AttributeTexture"].SetValue(chunk.RenderColor);
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

        internal void MaybeSetLifeRampParameters (EffectParameterCollection p) {
            var rt = p["LifeRampTexture"];
            if (rt == null)
                return;

            var lr = Configuration.Color.LifeRamp;
            var lifeRamp = lr?.Texture;
            if (lifeRamp != null)
                lifeRamp.EnsureInitialized(Engine.Configuration.FPTextureLoader);

            var lifeRampTexture =
                (lifeRamp != null)
                    ? lifeRamp.Instance
                    : null;
            rt.SetValue(lifeRampTexture);

            if ((lr != null) && (lifeRampTexture != null)) {
                var rangeSize = Math.Max(lr.Maximum - lr.Minimum, 0.001f);
                var indexDivisor = lifeRampTexture != null
                    ? lifeRampTexture.Height
                    : 1;
                p["LifeRampSettings"].SetValue(new Vector4(
                    lr.Strength * (lr.Invert ? -1 : 1),
                    lr.Minimum, rangeSize, indexDivisor
                ));
            } else {
                p["LifeRampSettings"].SetValue(new Vector4(
                    0, 0, 1, 1
                ));
            }
        }

        public void Render (
            IBatchContainer container, int layer,
            Material material = null,
            Matrix? transform = null, 
            BlendState blendState = null,
            ParticleRenderParameters renderParams = null,
            bool usePreviousData = false
        ) {
            var startedWhen = Time.Ticks;

            var appearance = Configuration.Appearance;
            if (appearance.Texture != null)
                appearance.Texture.EnsureInitialized(Engine.Configuration.TextureLoader);

            if (material == null) {
                if ((appearance.Texture != null) && (appearance.Texture.Instance != null)) {
                    material = appearance.Bilinear
                        ? Engine.ParticleMaterials.TextureLinear
                        : Engine.ParticleMaterials.TexturePoint;
                } else {
                    material = Engine.ParticleMaterials.NoTexture;
                }
            }

            // FIXME: Race condition
            if (blendState != null)
                material = Engine.Materials.Get(
                    material, blendState: blendState
                );
            var e = material.Effect;
            var p = e.Parameters;
            using (var group = BatchGroup.New(
                container, layer,
                Renderer.BeforeDraw, Renderer.AfterDraw, 
                userData: new InternalRenderParameters { Material = material, UserParameters = renderParams, DefaultMaterialSet = Engine.Materials }
            )) {
                RenderTrace.Marker(group, -9999, "Rasterize {0} particle chunks", Chunks.Count);

                int i = 1;
                foreach (var chunk in Chunks)
                    RenderChunk(group, chunk, material, i++, usePreviousData);
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            // FIXME: Release buffers
            foreach (var chunk in Chunks) {
                if (chunk.Previous != null)
                    Engine.DiscardedBuffers.Add(chunk.Previous);
                if (chunk.Current != null)
                    Engine.DiscardedBuffers.Add(chunk.Current);
                Engine.Coordinator.DisposeResource(chunk);
            }
            Engine.Systems.Remove(this);
            Engine.Coordinator.DisposeResource(LivenessQueryRT);
            LivenessInfos.Clear();
        }

        void IParticleSystems.Update (IBatchContainer container, int layer) {
            Update(container, layer);
        }
    }

    public class ParticleCollision {
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
        public float         Distance = 0.33f;

        /// <summary>
        /// Life of a particle decreases by this much every frame if it collides
        ///  with or is inside of a volume
        /// </summary>
        public float         LifePenalty = 0;

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
    }

    public class ParticleAppearance {
        public ParticleAppearance (Texture2D texture = null, string textureName = null) {
            Texture.Set(texture, textureName);
        }

        /// <summary>
        /// Configures the sprite used to render each particle.
        /// If null, the particle will be a solid-color quad
        /// </summary>
        public NullableLazyResource<Texture2D> Texture = new NullableLazyResource<Texture2D>();

        /// <summary>
        /// The offset into the texture at which the first frame of the sprite's animation begins.
        /// </summary>
        public Vector2 OffsetPx;

        /// <summary>
        /// The size of the section of the texture used by the particle (the whole texture is used by default).
        /// </summary>
        public Vector2? SizePx;

        /// <summary>
        /// Animates through the sprite texture based on the particle's life value, if set
        /// Smaller values will result in slower animation. Zero turns off animation.
        /// </summary>
        public Vector2 AnimationRate;

        /// <summary>
        /// Rounds the corners of the displayed particle (regardless of whether it has a texture).
        /// </summary>
        public bool Rounded;

        /// <summary>
        /// Applies a gamma curve to the opacity of circular particles
        /// </summary>
        public BezierF RoundingPowerFromLife = new BezierF(0.8f);

        /// <summary>
        /// Renders textured particles with bilinear filtering.
        /// </summary>
        public bool Bilinear = true;

        /// <summary>
        /// If true, the size of particles is relative to the size of their sprite texture.
        /// </summary>
        public bool RelativeSize = true;

        /// <summary>
        /// If true, the texture is treated as a spritesheet with each row representing a different angle of rotation.
        /// </summary>
        public bool RowFromVelocity = false;
        /// <summary>
        /// If true, the texture is treated as a spritesheet with each column representing a different angle of rotation.
        /// </summary>
        public bool ColumnFromVelocity = false;

        public Rectangle Rectangle {
            set {
                OffsetPx = new Vector2(value.X, value.Y);
                SizePx = new Vector2(value.Width, value.Height);
            }
        }
    }

    public class ParticleColorLifeRamp {
        /// <summary>
        /// Life values below this are treated as zero
        /// </summary>
        public float Minimum = 0.0f;

        /// <summary>
        /// Life values above this are treated as one
        /// </summary>
        public float Maximum = 100f;

        /// <summary>
        /// Blends between the constant color value for the particle and the color
        ///  from its life ramp
        /// </summary>
        public float Strength = 1.0f;

        /// <summary>
        /// If set, the life ramp has its maximum value at the left instead of the right.
        /// </summary>
        public bool  Invert;

        /// <summary>
        /// Specifies a color ramp texture
        /// </summary>
        public NullableLazyResource<Texture2D> Texture;
    }

    public class ParticleColor {
        internal Bezier4  _ColorFromLife = null;
        internal float?   _OpacityFromLife = null;

        /// <summary>
        /// Sets a global multiply color to apply to the particles
        /// </summary>
        public Vector4    Global = Vector4.One;

        public Bezier4 ColorFromVelocity = null;

        public ParticleColorLifeRamp LifeRamp;

        /// <summary>
        /// Multiplies the particle's opacity, producing a fade-in or fade-out based on the particle's life
        /// </summary>
        public float? OpacityFromLife {
            set {
                if (value == _OpacityFromLife)
                    return;

                _OpacityFromLife = value;
                if (value != null)
                    _ColorFromLife = null;
            }
            get {
                return _OpacityFromLife;
            }
        }

        /// <summary>
        /// Multiplies the particle's color, producing a fade-in or fade-out based on the particle's life
        /// </summary>
        public Bezier4 FromLife {
            get {
                return _ColorFromLife;
            }
            set {
                if (value == _ColorFromLife)
                    return;

                _ColorFromLife = value;
                if (value != null)
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
        /// Multiplies the particle's size, producing a shrink or grow based on the particle's life
        /// </summary>
        public BezierF       SizeFromLife = null;

        /// <summary>
        /// Multiplies the particle's size, producing a shrink or grow based on the speed of the particle
        /// </summary>
        public BezierF       SizeFromVelocity = null;

        /// <summary>
        /// Life of all particles decreases by this much every second
        /// </summary>
        public float         LifeDecayPerSecond = 1;

        /// <summary>
        /// Configures collision detection for particles
        /// </summary>
        public ParticleCollision Collision = new ParticleCollision(); 

        /// <summary>
        /// Particles will not be allowed to exceed this velocity
        /// </summary>
        public float         MaximumVelocity = 9999f;

        /// <summary>
        /// All particles will have their velocity reduced to roughly Velocity * (1.0 - Friction) every second
        /// </summary>
        public float         Friction = 0f;

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
        /// If set, particles will rotate based on their direction of movement
        /// </summary>
        public bool          RotationFromVelocity;

        /// <summary>
        /// Makes particles spin based on their life value
        /// </summary>
        public float         RotationFromLife = 0;

        /// <summary>
        /// Gives particles a constant rotation based on their index (pseudorandom-ish)
        /// </summary>
        public float         RotationFromIndex = 0;

        /// <summary>
        /// If set, the system's state will automatically be read into system memory after
        ///  every update
        /// </summary>
        [NonSerialized]
        public bool          AutoReadback = false;

        /// <summary>
        /// If set, the bitmap list created by readback will be sorted by particle's Z and Y values
        /// </summary>
        public bool          SortedReadback = true;

        public ParticleSystemConfiguration () {
        }

        public ParticleSystemConfiguration Clone () {
            var result = (ParticleSystemConfiguration)this.MemberwiseClone();
            return result;
        }
    }

    public class ParticleRenderParameters {
        public Vector2 Origin = Vector2.Zero;
        public Vector2 Scale = Vector2.One;
        public float? StippleFactor = null;
    }
}