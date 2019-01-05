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
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant.Particles {
    public class ParticleSystem : IDisposable {
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

            public RenderTargetBinding[] Bindings2, Bindings3, Bindings4;
            public RenderTarget2D PositionAndLife;
            public RenderTarget2D Velocity;

            public bool IsDisposed { get; private set; }
            public bool IsUpdateResult;

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

            public OcclusionQuery Query;
            public RenderTarget2D Color;

            internal RenderTarget2D RenderData, RenderColor;
            internal BufferSet LastUpdateResult;

            public bool NoLongerASpawnTarget { get; internal set; }
            public bool IsFeedbackOutput { get; internal set; }
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
                Query = new OcclusionQuery(device);
                Color = new RenderTarget2D(
                    device, Size, Size, false, SurfaceFormat.Vector4, 
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                );
                RenderData = new RenderTarget2D(
                    device, Size, Size, false, SurfaceFormat.Vector4, 
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                );
                RenderColor = new RenderTarget2D(
                    device, Size, Size, false, SurfaceFormat.Vector4, 
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                );
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                if (LastUpdateResult != null)
                    LastUpdateResult.IsUpdateResult = false;

                IsDisposed = true;

                LastUpdateResult = null;
                Color.Dispose();
                RenderData.Dispose();
                RenderColor.Dispose();
                Query.Dispose();
                Query = null;
            }

            internal void Clear () {
                if (LastUpdateResult != null)
                    LastUpdateResult.IsUpdateResult = false;

                LastUpdateResult = null;
                NextSpawnOffset = 0;
                TotalConsumedForFeedback = 0;
                TotalSpawned = 0;
                IsFeedbackOutput = false;
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

            public float? OverrideStippleFactor { get; internal set; }

            public RenderHandler (ParticleSystem system) {
                System = system;
                BeforeDraw = _BeforeDraw;
                AfterDraw = _AfterDraw;
            }

            private void _BeforeDraw (DeviceManager dm, object _m) {
                var m = (Material)_m;
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
                        p["BitmapTextureRegion"].SetValue(new Vector4(
                            offset.X, offset.Y, 
                            offset.X + size.X, offset.Y + size.Y
                        ));
                    }
                }

                var cfr = p["ColumnFromVelocity"];
                if (cfr != null) {
                    cfr.SetValue(appearance.ColumnFromVelocity);
                    p["RowFromVelocity"].SetValue(appearance.RowFromVelocity);
                }

                var f = p["BitmapBilinear"];
                if (f != null)
                    f.SetValue(appearance.Bilinear);

                var sf = p["SizeFactor"];
                if (sf != null) {
                    if ((tex != null) && appearance.RelativeSize) {
                        var frameTexSize = appearance.SizePx.GetValueOrDefault(texSize) * 0.5f;
                        sf.SetValue(frameTexSize);
                    } else
                        sf.SetValue(Vector2.One);
                }

                System.MaybeSetAnimationRateParameter(p, appearance);

                sf = p["StippleFactor"];
                if (sf != null)
                    sf.SetValue(OverrideStippleFactor.GetValueOrDefault(System.Configuration.StippleFactor));

                var pc = p["Rounded"];
                if (pc != null)
                    pc.SetValue((appearance?.Rounded).GetValueOrDefault(false));

                var cp = p["RoundingPower"];
                if (cp != null)
                    cp.SetValue((appearance?.RoundingPower).GetValueOrDefault(2));

                var gc = p["GlobalColor"];
                if (gc != null) {
                    var gcolor = System.Configuration.Color.Global;
                    gcolor.X *= gcolor.W;
                    gcolor.Y *= gcolor.W;
                    gcolor.Z *= gcolor.W;
                    gc.SetValue(gcolor);
                }

                System.MaybeSetLifeRampParameters(p);
            }

            private void _AfterDraw (DeviceManager dm, object _m) {
                var m = (Material)_m;
                var e = m.Effect;
                var p = e.Parameters;
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
        }

        public bool IsDisposed { get; private set; }
        public int LiveCount { get; private set; }

        public readonly ParticleEngine                     Engine;
        public readonly ParticleSystemConfiguration        Configuration;
        public readonly List<Transforms.ParticleTransform> Transforms = 
            new List<Transforms.ParticleTransform>();

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
        public event Action<ParticleSystem> OnDeviceReset;

        // HACK: Performing occlusion queries every frame seems to be super unreliable,
        //  so just perform them intermittently and accept that our data will be outdated
        public const int LivenessCheckInterval = 4;
        private int FramesUntilNextLivenessCheck = LivenessCheckInterval;

        private double? LastUpdateTimeSeconds = null;
        private double  UpdateErrorAccumulator = 0;

        private readonly RenderTarget2D LivenessQueryRT;

        public long TotalSpawnCount { get; private set; }

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
            Renderer = new RenderHandler(this);
            Updater = new Transforms.ParticleTransform.UpdateHandler(null);

            engine.Systems.Add(this);

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

        internal Chunk PickTargetForSpawn (
            bool feedback, int count, 
            ref int currentTarget, out bool needClear
        ) {
            var chunk = ChunkFromID(currentTarget);
            // FIXME: Ideally we could split the spawn across this chunk and an old one.
            if (chunk != null) {
                if (chunk.Free < count) {
                    chunk.NoLongerASpawnTarget = true;
                    currentTarget = -1;
                    chunk = null;
                }
            }

            if (chunk == null) {
                chunk = CreateChunk();
                chunk.IsFeedbackOutput = feedback;
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
                c => (c.AvailableForFeedback >= count / 2) && !c.IsFeedbackOutput
            );
            if (newChunk != null)
                CurrentFeedbackSource = newChunk.ID;
            return newChunk;
        }

        private void RunSpawner (
            IBatchContainer container, ref int layer, Material m,
            long startedWhen, Transforms.SpawnerBase spawner,
            Transforms.ParameterSetter setParameters,
            double deltaTimeSeconds, float now
        ) {
            int spawnCount = 0, requestedSpawnCount;

            if (!spawner.IsValid)
                return;

            Chunk sourceChunk;
            spawner.BeginTick(this, now, deltaTimeSeconds, out requestedSpawnCount, out sourceChunk);

            if (requestedSpawnCount <= 0) {
                return;
            } else if (requestedSpawnCount > ChunkMaximumCount)
                spawnCount = ChunkMaximumCount;
            else
                spawnCount = requestedSpawnCount;

            bool needClear;
            var chunk = PickTargetForSpawn(spawner is Transforms.FeedbackSpawner, spawnCount, out needClear);

            if (chunk == null)
                throw new Exception("Failed to locate or create a chunk to spawn in");

            var first = chunk.NextSpawnOffset;
            var last = chunk.NextSpawnOffset + spawnCount - 1;
            spawner.SetIndices(first, last);
            // Console.WriteLine("Spawning {0}-{1} free {2}", first, last, spawnState.Free);

            chunk.NextSpawnOffset += spawnCount;
            TotalSpawnCount += spawnCount;
            if (sourceChunk != null)
                sourceChunk.TotalConsumedForFeedback += spawnCount;

            spawner.EndTick(requestedSpawnCount, spawnCount);
            chunk.TotalSpawned += spawnCount;

            RunTransform(
                chunk, container, ref layer, m,
                startedWhen, true,
                spawner.Handler.BeforeDraw, spawner.Handler.AfterDraw, 
                deltaTimeSeconds, needClear, now, false,
                sourceChunk
            );
        }

        private void RunTransform (
            Chunk chunk, IBatchContainer container, ref int layer, Material m,
            long startedWhen, bool isSpawning,
            Action<DeviceManager, object> beforeDraw,
            Action<DeviceManager, object> afterDraw,
            double deltaTimeSeconds, bool shouldClear,
            float now, bool isUpdate, Chunk sourceChunk
        ) {
            if (chunk == null)
                throw new ArgumentNullException();

            var device = container.RenderManager.DeviceManager.Device;

            var e = m.Effect;
            var p = (e != null) ? e.Parameters : null;

            var li = GetLivenessInfo(chunk);
            UpdateChunkLivenessQuery(li);

            var chunkMaterial = m;
            if (isSpawning)
                li.DeadFrameCount = 0;
            else
                RotateBuffers(chunk);

            var prev = chunk.Previous;
            var curr = chunk.Current;

            if (prev != null)
                prev.LastTurnUsed = Engine.CurrentTurn;
            curr.LastTurnUsed = Engine.CurrentTurn;

            if (e != null)
                RenderTrace.Marker(container, layer++, "Particle transform {0}", e.CurrentTechnique.Name);

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
                Now = now,
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
                if (chunk.LastUpdateResult != null)
                    chunk.LastUpdateResult.IsUpdateResult = false;
                chunk.LastUpdateResult = curr;
                curr.IsUpdateResult = true;
            }
        }

        public void Reset () {
            LastClearTimestamp = Time.Ticks;
            IsClearPending = true;
            TotalSpawnCount = 0;
            UpdateErrorAccumulator = 0;
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
            chunk.Clear();
            Engine.Coordinator.DisposeResource(chunk);
        }

        internal void SetSystemUniforms (Material m, double deltaTimeSeconds) {
            ClampedBezier4 colorFromLife, colorFromVelocity;
            ClampedBezier1 sizeFromLife, sizeFromVelocity;

            var psu = new Uniforms.ParticleSystem(Engine.Configuration, Configuration, deltaTimeSeconds);
            Engine.ParticleMaterials.MaterialSet.TrySetBoundUniform(m, "System", ref psu);

            var o = Configuration.Color._OpacityFromLife.GetValueOrDefault(0);
            if (o != 0) {
                colorFromLife = new ClampedBezier4 {
                    A = new Vector4(1, 1, 1, 0),
                    B = Vector4.One,
                    RangeAndCount = new Vector4(0, 1.0f / o, 2, 1)
                };
            } else {
                colorFromLife = new ClampedBezier4(Configuration.Color._ColorFromLife);
            }
            colorFromVelocity = new ClampedBezier4(Configuration.Color.ColorFromVelocity);

            sizeFromLife = new ClampedBezier1(Configuration.SizeFromLife);
            sizeFromVelocity = new ClampedBezier1(Configuration.SizeFromVelocity);

            Engine.ParticleMaterials.MaterialSet.TrySetBoundUniform(m, "ColorFromLife", ref colorFromLife);
            Engine.ParticleMaterials.MaterialSet.TrySetBoundUniform(m, "SizeFromLife", ref sizeFromLife);
            Engine.ParticleMaterials.MaterialSet.TrySetBoundUniform(m, "ColorFromVelocity", ref colorFromVelocity);
            Engine.ParticleMaterials.MaterialSet.TrySetBoundUniform(m, "SizeFromVelocity", ref sizeFromVelocity);
        }

        private BufferSet AcquireOrCreateBufferSet () {
            BufferSet result;
            if (!Engine.AvailableBuffers.TryPopFront(out result))
                result = CreateBufferSet(Engine.Coordinator.Device);
            result.LastTurnUsed = Engine.CurrentTurn;
            return result;
        }

        private void RotateBuffers (Chunk chunk) {
            Engine.NextTurn();

            var prev = chunk.Previous;
            chunk.Previous = chunk.Current;
            chunk.Current = AcquireOrCreateBufferSet();
            if (prev != null)
                Engine.DiscardedBuffers.Add(prev);
        }

        public void Update (IBatchContainer container, int layer, float? deltaTimeSeconds = null) {
            var lastUpdateTimeSeconds = LastUpdateTimeSeconds;
            var updateError = UpdateErrorAccumulator;
            UpdateErrorAccumulator = 0;
            var now = (float)(LastUpdateTimeSeconds = TimeProvider.Seconds);
            CurrentFrameIndex++;

            var ups = Engine.Configuration.UpdatesPerSecond;

            var tickUnit = 1.0 / ups.GetValueOrDefault(60);
            var actualDeltaTimeSeconds = tickUnit;
            if (deltaTimeSeconds.HasValue)
                actualDeltaTimeSeconds = deltaTimeSeconds.Value;
            else if (lastUpdateTimeSeconds.HasValue)
                actualDeltaTimeSeconds = Math.Min(
                    LastUpdateTimeSeconds.Value - lastUpdateTimeSeconds.Value, 
                    Engine.Configuration.MaximumUpdateDeltaTimeSeconds
                );

            if (ups.HasValue) {
                actualDeltaTimeSeconds += updateError;
                var tickCount = (int)Math.Floor(actualDeltaTimeSeconds / tickUnit);
                var adjustedDeltaTime = tickCount * tickUnit;
                var newError = actualDeltaTimeSeconds - adjustedDeltaTime;
                UpdateErrorAccumulator = newError;
                actualDeltaTimeSeconds = adjustedDeltaTime;
            }

            actualDeltaTimeSeconds = Math.Min(
                actualDeltaTimeSeconds, Engine.Configuration.MaximumUpdateDeltaTimeSeconds
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

                foreach (var s in Transforms.OfType<Transforms.SpawnerBase>()) {
                    if (!s.IsActive)
                        continue;

                    var it = (Transforms.IParticleTransform)s;
                    RunSpawner(
                        group, ref i, it.GetMaterial(Engine.ParticleMaterials),
                        startedWhen, s,
                        it.SetParameters, actualDeltaTimeSeconds, now
                    );
                }

                foreach (var chunk in Chunks)
                    UpdateChunk(chunk, now, (float)actualDeltaTimeSeconds, startedWhen, pm, group, ref i, computingLiveness);

                if (computingLiveness)
                    ComputeLiveness(group, i++);
            }

            var ts = Time.Ticks;

            Engine.EndOfUpdate(initialTurn);
        }

        private void UpdateChunk (
            Chunk chunk, float now, 
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
                    startedWhen, false, it.BeforeDraw, it.AfterDraw,
                    actualDeltaTimeSeconds, isFirstXform, now, false, null
                );

                isFirstXform = false;
            }

            if (IsClearPending) {
                // occlusion queries suck and never work right, and for some reason
                //  the old particle data is a ghost from hell and refuses to disappear
                //  even after it is cleared
                for (int k = 0; k < 2; k++) {
                    RunTransform(
                        chunk, group, ref i, pm.Erase,
                        startedWhen, false,
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
                    startedWhen, false,
                    Updater.BeforeDraw, Updater.AfterDraw, actualDeltaTimeSeconds, true, now, true, null
                );
            } else {
                RunTransform(
                    chunk, group, ref i, pm.UpdatePositions,
                    startedWhen, false, Updater.BeforeDraw, Updater.AfterDraw,
                    actualDeltaTimeSeconds, true, now, true, null
                );
            }
        }

        private void ComputeLiveness (
            BatchGroup group, int layer
        ) {
            var quadCount = ChunkMaximumCount;

            using (var rtg = BatchGroup.ForRenderTarget(
                group, layer, LivenessQueryRT, (dm, _) => {
                    dm.Device.Clear(Color.White);
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
                            dm.Device.Clear(Color.Transparent);
                            SetSystemUniforms(m, 0);

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
            BatchGroup group, Chunk chunk, Material m, int layer
        ) {
            // TODO: Actual occupied count?
            var quadCount = ChunkMaximumCount;
            var curr = chunk.Current;

            if (curr == null)
                return;

            curr.LastTurnUsed = Engine.CurrentTurn;

            // Console.WriteLine("Draw {0}", chunk.ID);

            using (var batch = NativeBatch.New(
                group, layer, m, (dm, _) => {
                    var p = m.Effect.Parameters;
                    p["PositionTexture"].SetValue(curr.PositionAndLife);
                    // HACK
                    p["VelocityTexture"].SetValue(chunk.RenderData);
                    p["AttributeTexture"].SetValue(chunk.RenderColor);
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
            float? overrideStippleFactor = null
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
            Renderer.OverrideStippleFactor = overrideStippleFactor;
            var m = Engine.Materials.Get(
                material, blendState: blendState
            );
            var e = m.Effect;
            var p = e.Parameters;
            using (var group = BatchGroup.New(
                container, layer,
                Renderer.BeforeDraw, Renderer.AfterDraw, m
            )) {
                RenderTrace.Marker(group, -9999, "Rasterize {0} particle chunks", Chunks.Count);

                int i = 1;
                foreach (var chunk in Chunks)
                    RenderChunk(group, chunk, m, i++);
            }
        }

        internal void MaybeSetAnimationRateParameter (EffectParameterCollection p, ParticleAppearance appearance) {
            var parm = p["AnimationRateAndRotationAndZToY"];
            if (parm == null)
                return;
            var ar = appearance != null ? appearance.AnimationRate : Vector2.Zero;
            var arv = new Vector4(
                (ar.X != 0) ? 1.0f / ar.X : 0, (ar.Y != 0) ? 1.0f / ar.Y : 0,
                Configuration.RotationFromVelocity ? 1f : 0f, Configuration.ZToY
            );
            parm.SetValue(arv);
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
        public float RoundingPower = 0.8f;

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

        public ParticleSystemConfiguration () {
        }

        public ParticleSystemConfiguration Clone () {
            var result = (ParticleSystemConfiguration)this.MemberwiseClone();
            return result;
        }
    }
}