﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
using Squared.Render.Evil;
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant.Particles {
    /// <summary>
    /// Fills the buffer with data for up to count particles.
    /// </summary>
    /// <returns>The maximum life value of the spawned particles.</returns>
    public delegate float ParticleBufferInitializer<TElement> (TElement[] buffer, int count);

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

    public partial class ParticleSystem : IParticleSystems {
        public const int MaxChunkCount = 64;

        public class UpdateResult {
            public ParticleSystem System { get; private set; }
            public bool PerformedUpdate { get; private set; }
            public float Timestamp { get; internal set; }

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

        internal class BufferSet : IDisposable {
            public readonly int Size, MaximumCount;
            public readonly int ID;

            // The ID of the chunk that used the buffer this frame (if any)
            public int CurrentOwnerID;
            // Once a given chunk uses a buffer, it owns it for the rest of the frame
            // This prevents other systems or chunks from accidentally trampling over state
            public int CurrentOwnerFrameIndex;
            // This tracks whether a buffer is currently being read from (for readback/rendering)
            public int  LastFrameDependency = -1;
            // This tracks the last turn a buffer was used (note that turns are NOT frames)
            public long LastTurnUsed;
            public RenderTarget2D PositionAndLife;
            public RenderTarget2D Velocity;

            public bool IsDisposed { get; private set; }

            private static volatile int NextID;

            private static readonly RenderTargetBinding[] Bindings2 = new RenderTargetBinding[2], 
                Bindings3 = new RenderTargetBinding[3], 
                Bindings4 = new RenderTargetBinding[4];

            public BufferSet (ParticleEngineConfiguration configuration, GraphicsDevice device) {
                ID = Interlocked.Increment(ref NextID);
                Size = configuration.ChunkSize;
                MaximumCount = Size * Size;

                PositionAndLife = CreateRenderTarget(configuration, device);
                Velocity = CreateRenderTarget(configuration, device);
            }

            internal RenderTarget2D CreateRenderTarget (ParticleEngineConfiguration configuration, GraphicsDevice device) {
                return new RenderTarget2D(
                    device,
                    Size, Size, false,
                    SurfaceFormat.Vector4, DepthFormat.None,
                    0, RenderTargetUsage.PreserveContents
                ) {
                    Name = "ParticleSystem.BufferSet",
                };
            }

            public void Dispose () {
                if (IsDisposed)
                    return;

                IsDisposed = true;
                PositionAndLife.Dispose();
                Velocity.Dispose();
            }

            internal void PushRenderTargets (ref DeviceManager dm) {
                Bindings2[0] = new RenderTargetBinding(PositionAndLife);
                Bindings2[1] = new RenderTargetBinding(Velocity);
                dm.PushRenderTargets(Bindings2);
            }

            internal void PushRenderTargets (ref DeviceManager dm, RenderTarget2D binding3) {
                Bindings3[0] = new RenderTargetBinding(PositionAndLife);
                Bindings3[1] = new RenderTargetBinding(Velocity);
                Bindings3[2] = new RenderTargetBinding(binding3);
                dm.PushRenderTargets(Bindings3);
            }

            internal void PushRenderTargets (ref DeviceManager dm, RenderTarget2D binding3, RenderTarget2D binding4) {
                Bindings4[0] = new RenderTargetBinding(PositionAndLife);
                Bindings4[1] = new RenderTargetBinding(Velocity);
                Bindings4[2] = new RenderTargetBinding(binding3);
                Bindings4[3] = new RenderTargetBinding(binding4);
                dm.PushRenderTargets(Bindings4);
            }
        }

        public class Chunk : IDisposable {
            public readonly ParticleSystem System;

            public long GlobalIndexOffset;
            public readonly int Size, MaximumCount;
            public int ID;
            public int RefCount;

            internal bool CurrentIsNew;
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

            public RenderTarget2D LifeReadTexture { get; internal set; }

            private static volatile int NextID;

            internal volatile float ApproximateMaximumLife;

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
                ) {
                    Name = "ParticleSystem.Chunk",
                };
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
                var rp = _rp as ParticleRenderParameters;
                var m = rp?.Material ?? (_rp as Material);
                if (m == null)
                    return;
                var p = m.Parameters;

                // FIXME: deltaTime
                System.SetSystemUniforms(m, 0);

                var origin = rp?.Origin ?? Vector2.Zero;
                var scale = rp?.Scale ?? Vector2.One;

                var u = new RasterizeParticleSystem(System.Engine.Configuration, System.Configuration, origin, scale);
                System.Engine.uRasterize.TrySet(m, ref u);

                if (p != null)
                    System.MaybeSetLifeRampParameters(p);
            }

            private void _AfterDraw (DeviceManager dm, object _rp) {
                var rp = _rp as ParticleRenderParameters;
                var m = rp?.Material ?? (_rp as Material);
                if (m == null)
                    return;
                m?.Parameters.ClearTextures(System.Engine.Coordinator.Manager, ClearTextureList);
            }
        }

        internal static readonly string[] PreClearTextureList = new[] {
            "PositionTexture", "VelocityTexture", "AttributeTexture",
        };

        internal static readonly string[] ClearTextureList = new[] {
            "PositionTexture", "VelocityTexture", "AttributeTexture",
            "LifeRampTexture", "BitmapTexture", "RampTexture",
            "RandomnessTexture", "LowPrecisionRandomnessTexture",
            "PatternTexture", "DistanceFieldTexture"
        };

        public bool IsDisposed { get; private set; }
        public int LiveCount { get; private set; }
        public string Label;

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
        private int LastFrameUpdated = -1;
        public event Action<ParticleSystem> OnDeviceReset;

        private double? LastUpdateTimeSeconds = null;
        private double  UpdateErrorAccumulator = 0;

        public long TotalSpawnCount { get; private set; }

        private readonly Action<DeviceManager, object> BeforeSystemUpdate, AfterSystemUpdate, RenderChunkSetup;
        private readonly Action UpdateAfterPresentHandler;
        private readonly SendOrPostCallback AfterFrameHandler;

        private SynchronizationContext LastUpdateSyncContext;

        private readonly UnorderedList<Transforms.ParticleTransformUpdateParameters> _UpdateParameterPool = 
            new UnorderedList<Particles.Transforms.ParticleTransformUpdateParameters>();
        private readonly UnorderedList<Transforms.ParticleTransformUpdateParameters> _UpdateParametersInUse = 
            new UnorderedList<Particles.Transforms.ParticleTransformUpdateParameters>();

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

            BeforeSystemUpdate = _BeforeSystemUpdate;
            AfterSystemUpdate = _AfterSystemUpdate;
            AfterFrameHandler = _AfterFrameHandler;
            UpdateAfterPresentHandler = _UpdateAfterPresentHandler;

            engine.Systems.Add(this);
        }

        public ITimeProvider TimeProvider {
            get {
                return Configuration.TimeProvider ?? (Engine.Configuration.TimeProvider ?? Time.DefaultTimeProvider);
            }
        }

        public int Capacity {
            get {
                // FIXME
                lock (Chunks) {
                    if (Engine.Configuration.AccurateLivenessCounts)
                        return Chunks.Count * ChunkMaximumCount;
                    else
                        return Chunks.Count;
                }
            }
        }

        private Chunk ChunkFromID (int id) {
            lock (Chunks)
            foreach (var c in Chunks)
                if (c.ID == id)
                    return c;

            return null;
        }

        private BufferSet CreateBufferSet (GraphicsDevice device) {
            lock (Engine.Coordinator.CreateResourceLock) {
                var result = new BufferSet(Engine.Configuration, device);
                Engine.AllBuffers.Add(result);
                return result;
            }
        }
        
        private Chunk CreateChunk (int frameIndex) {
            lock (Chunks)
            if (Chunks.Count >= MaxChunkCount)
                return null;

            lock (Engine.Coordinator.CreateResourceLock) {
                var result = new Chunk(this);
                result.Current = AcquireOrCreateBufferSet(result.ID, frameIndex, out result.CurrentIsNew);
                result.GlobalIndexOffset = TotalSpawnCount;
                return result;
            }
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

        private void RunTransform (
            Chunk chunk, IBatchContainer container, ref int layer, Material m,
            long startedWhen, bool isSpawning, bool isAnalyzer,
            Action<DeviceManager, object> beforeDraw,
            Action<DeviceManager, object> afterDraw,
            double deltaTimeSeconds, bool shouldClear,
            double now, bool isUpdate, Chunk sourceChunk, string label
        ) {
            if (chunk == null)
                throw new ArgumentNullException();

            var device = container.RenderManager.DeviceManager.Device;

            var li = GetLivenessInfo(chunk);
            // FIXME
            if (li == null)
                return;

            var chunkMaterial = m;
            bool currentIsNew = false;
            if (isSpawning)
                li.DeadFrameCount = 0;
            else if (!isAnalyzer)
                RotateBuffers(chunk, container.RenderManager.DeviceManager.FrameIndex, out currentIsNew);

            var prev = chunk.Previous;
            var curr = chunk.Current;

            if (prev != null)
                prev.LastTurnUsed = Engine.CurrentTurn;
            curr.LastTurnUsed = Engine.CurrentTurn;

            if ((m != null) && RenderTrace.EnableTracing) {
                var nameText = (label != null)
                    ? $"{label} {m.Name}"
                    : m.Name;
                RenderTrace.Marker(container, layer++, "System {0:X8} Transform {1} Chunk {2} Previous {3} Current {4}", GetHashCode(), nameText, chunk.ID, prev?.ID, curr?.ID);
            }

            Transforms.ParticleTransformUpdateParameters up;
            if (!_UpdateParameterPool.TryPopBack(out up))
                up = new Particles.Transforms.ParticleTransformUpdateParameters();
            _UpdateParametersInUse.Add(up);

            up.System = this;
            up.Material = m;
            up.Prev = prev;
            up.Curr = curr;
            up.IsUpdate = isUpdate;
            up.IsSpawning = isSpawning;
            up.ShouldClear = shouldClear;
            up.Chunk = chunk;
            up.SourceChunk = sourceChunk;
            up.SourceData = sourceChunk?.Previous ?? sourceChunk?.Current;
            up.Now = (float)now;
            up.DeltaTimeSeconds = deltaTimeSeconds;
            up.CurrentFrameIndex = CurrentFrameIndex;

            if (currentIsNew || chunk.CurrentIsNew) {
                ClearBatch.AddNew(container, layer++, Engine.Materials.Clear, clearColor: Color.Transparent);
                chunk.CurrentIsNew = false;
            }

            using (var batch = NativeBatch.New(
                container, layer++, m,
                beforeDraw,
                afterDraw, up
            ))  {
                if (m != null)
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

        /// <summary>
        /// Resets internal state and clears resources
        /// </summary>
        public void Reset () {
            ReleaseResources();
            Engine.ResetCount++;
            TotalSpawnCount = 0;
            UpdateErrorAccumulator = 0;
            CurrentFrameIndex = 0;
            LastUpdateTimeSeconds = null;
            foreach (var xform in Transforms)
                xform.Reset();
        }

        /// <summary>
        /// Clears resources like textures, but does not reset internal state
        /// This will also clear any active particles
        /// </summary>
        public void ReleaseResources () {
            LastClearTimestamp = Time.Ticks;
            IsClearPending = true;
            lock (ChunksToReap) {
                foreach (var chunk in Chunks) {
                    var li = GetLivenessInfo(chunk);

                    if (li != null)
                        ChunksToReap.Add(li);
                }
            }
            LiveCount = 0;
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

        private BufferSet AcquireOrCreateBufferSet (int ownerId, int frameIndex, out bool resultIsNew) {
            BufferSet result = null;
            resultIsNew = true;

            using (var e = Engine.AvailableBuffers.GetEnumerator()) {
                while (e.GetNext(out BufferSet ab)) {
                    if ((ab.CurrentOwnerID != 0) && (ab.CurrentOwnerID != ownerId))
                        continue;

                    result = ab;
                    e.RemoveCurrent();
                    resultIsNew = false;
                    break;
                }
            }

            if (result == null)
                result = CreateBufferSet(Engine.Coordinator.Device);

            result.CurrentOwnerFrameIndex = frameIndex;
            result.LastTurnUsed = Engine.CurrentTurn;
            result.CurrentOwnerID = ownerId;
            return result;
        }

        private void RotateBuffers (Chunk chunk, int frameIndex, out bool resultIsNew) {
            Engine.NextTurn(frameIndex);

            var prev = chunk.Previous;
            if (prev != null) {
#if DEBUG
                if (prev.CurrentOwnerID != chunk.ID)
                    throw new InvalidOperationException("Chunk owner was changed");
#endif
                Engine.DiscardedBuffers.Add(prev);
            }

            chunk.Previous = chunk.Current;
            chunk.Current = AcquireOrCreateBufferSet(chunk.ID, frameIndex, out resultIsNew);
        }

        private void _BeforeSystemUpdate (DeviceManager dm, object userData) {
            lock (ReadbackLock)
            if (Configuration.AutoReadback)
                ReadbackFuture = new Future<ArraySegment<BitmapDrawCall>>();
            dm.Device.DepthStencilState = DepthStencilState.None;
        }

        private void _AfterSystemUpdate (DeviceManager dm, object userData) {
            var now = TimeProvider.Seconds;
            MaybePerformReadback((float)now);
        }

        public void Update (IBatchContainer container, int layer) {
            Update(container, layer, false);
        }

        public UpdateResult Update (IBatchContainer container, int layer, bool needResultObject) {
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
                    return needResultObject ? new UpdateResult(this, false, (float)now) : null;
                LastUpdateTimeSeconds = now = lastUpdateTimeSeconds.Value + adjustedDeltaTime;
            } else {
                LastUpdateTimeSeconds = now;
            }

            LastFrameUpdated = container.RenderManager.DeviceManager.FrameIndex;

            actualDeltaTimeSeconds = Math.Min(actualDeltaTimeSeconds, maxDeltaTime);

            // Console.WriteLine(actualDeltaTimeSeconds);

            var startedWhen = Time.Ticks;

            UpdateLiveCountAndReapDeadChunks();

            var initialTurn = Engine.CurrentTurn;

            var pm = Engine.ParticleMaterials;

            lock (_UpdateParameterPool)
            using (var group = BatchGroup.ForRenderTarget(
                container, layer, (RenderTarget2D)null,
                BeforeSystemUpdate,
                AfterSystemUpdate,
                name: "Update particle system"
            )) {
                int i = 0;

                lock (NewUserChunks) {
                    foreach (var nc in NewUserChunks) {
                        nc.GlobalIndexOffset = TotalSpawnCount;
                        nc.NoLongerASpawnTarget = true;
                        TotalSpawnCount += nc.MaximumCount;
                        lock (Chunks)
                            Chunks.Add(nc);
                    }

                    NewUserChunks.Clear();
                }

                if (IsClearPending) {
                    var chunkList = default(DenseList<Chunk>);
                    lock (Chunks)
                        chunkList.AddRange(Chunks);
                    foreach (var c in chunkList) {
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

                foreach (var _s in Transforms) {
                    if (!(_s is Transforms.SpawnerBase s))
                        continue;
                    if (!s.IsActive || !s.IsActive2)
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

                lock (Chunks)
                foreach (var chunk in Chunks)
                    UpdateChunk(chunk, now, (float)actualDeltaTimeSeconds, startedWhen, pm, group, ref i, computingLiveness);

                if (computingLiveness)
                    ComputeLiveness(container, layer);
            }

            var ts = Time.Ticks;

            LastUpdateSyncContext = SynchronizationContext.Current;
            Engine.Coordinator.AfterPresent(UpdateAfterPresentHandler);

            Engine.EndOfUpdate(container, layer, initialTurn, container.RenderManager.DeviceManager.FrameIndex);

            LastResetCount = Engine.ResetCount;

            return needResultObject ? new UpdateResult(this, true, (float)now) : null;
        }

        private void _AfterFrameHandler (object _) {
            foreach (var t in Transforms)
                t.AfterFrame(Engine);

            lock (_UpdateParameterPool) {
                foreach (var up in _UpdateParametersInUse) {
                    if (_UpdateParameterPool.Count < 16)
                        _UpdateParameterPool.Add(up);
                }
                _UpdateParametersInUse.Clear();
            }
        }

        private void _UpdateAfterPresentHandler () {
            var sc = LastUpdateSyncContext;

            if (sc != null)
                sc.Post(AfterFrameHandler, null);
            else
                _AfterFrameHandler(null);
        }

        internal void NotifyDeviceReset () {
            if (OnDeviceReset != null)
                OnDeviceReset(this);
            ReleaseResources();
        }

        private void UpdateChunk (
            Chunk chunk, double now, 
            float actualDeltaTimeSeconds, long startedWhen, 
            ParticleMaterials pm, BatchGroup group, 
            ref int i, bool computingLiveness
        ) {
            var didRunAnyTransforms = false;

            var isFirstXform = true;
            foreach (var t in Transforms) {
                var it = (Transforms.IParticleTransform)t;

                var shouldSkip = !t.IsActive || !t.IsActive2 || (t is Transforms.SpawnerBase) || !t.IsValid;
                if (shouldSkip)
                    continue;

                didRunAnyTransforms = true;
                RunTransform(
                    chunk, group, ref i, it.GetMaterial(Engine.ParticleMaterials),
                    startedWhen, false, it.IsAnalyzer, it.BeforeDraw, it.AfterDraw,
                    actualDeltaTimeSeconds, isFirstXform && !it.IsAnalyzer, now, false, null,
                    t.Label
                );

                if (!it.IsAnalyzer)
                    isFirstXform = false;
            }

            if (IsClearPending) {
                // occlusion queries suck and never work right, and for some reason
                //  the old particle data is a ghost from hell and refuses to disappear
                //  even after it is cleared
                didRunAnyTransforms = true;
                for (int k = 0; k < 2; k++) {
                    RunTransform(
                        chunk, group, ref i, pm.Erase,
                        startedWhen, false, false,
                        null, null, actualDeltaTimeSeconds, 
                        true, now, true, null, Label
                    );
                }

                // FIXME: Still fucked
            } else if (Configuration.Collision?.DistanceField != null) {
                if (Configuration.Collision.DistanceFieldMaximumZ == null)
                    throw new InvalidOperationException("If a distance field is active, you must set DistanceFieldMaximumZ");

                didRunAnyTransforms = true;
                RunTransform(
                    chunk, group, ref i, pm.UpdateWithDistanceField,
                    startedWhen, false, false,
                    Updater.BeforeDraw, Updater.AfterDraw, 
                    actualDeltaTimeSeconds, true, now, true, null, Label
                );
                chunk.ApproximateMaximumLife -= Configuration.LifeDecayPerSecond * actualDeltaTimeSeconds;
            } else {
                didRunAnyTransforms = true;
                RunTransform(
                    chunk, group, ref i, pm.UpdatePositions,
                    startedWhen, false, false, 
                    Updater.BeforeDraw, Updater.AfterDraw,
                    actualDeltaTimeSeconds, true, now, true, null, Label
                );
                chunk.ApproximateMaximumLife -= Configuration.LifeDecayPerSecond * actualDeltaTimeSeconds;
            }
        }

        private void CopyBuffer (IBatchContainer container, ref int layer, RenderTarget2D from, RenderTarget2D to) {
            using (var group = BatchGroup.ForRenderTarget(container, ++layer, to, name: "Copy Particle System Buffer"))
            using (var bb = BitmapBatch.New(group, 0, Engine.Materials.GetBitmapMaterial(false, RasterizerState.CullNone, DepthStencilState.None, BlendState.Opaque), samplerState: SamplerState.PointClamp))
                bb.Add(new BitmapDrawCall(from, Vector2.Zero));
        }

        private void AutoGrowBuffer<T> (ref T[] buffer, int size) {
            if ((buffer == null) || (buffer.Length < size))
                buffer = new T[size];
        }

        private bool IsChunkValidSource (BufferSet src, Chunk chunk) {
            var isValid = AutoRenderTarget.IsRenderTargetValid(src.PositionAndLife) &&
                AutoRenderTarget.IsRenderTargetValid(chunk.RenderData) &&
                AutoRenderTarget.IsRenderTargetValid(chunk.RenderColor);
            return isValid;
        }

        private void RenderChunk (
            BatchGroup group, Chunk chunk, Material m, int layer, bool usePreviousData
        ) {
            // TODO: Actual occupied count?
            var quadCount = Math.Min(ChunkMaximumCount, chunk.TotalSpawned + 1);

            var src = usePreviousData ? chunk.Previous : chunk.Current;

            if (RenderTrace.EnableTracing)
                RenderTrace.Marker(group, layer++, "System {0:X8} {3} Render Chunk {1} Current {2}", GetHashCode(), chunk.ID, chunk.Current.ID, Label);

            using (var batch = NativeBatch.New(
                group, layer, m
            )) {
                batch.MaterialParameters.Add("PositionTexture", src.PositionAndLife);
                batch.MaterialParameters.Add("VelocityTexture", chunk.RenderData);
                batch.MaterialParameters.Add("AttributeTexture", chunk.RenderColor);

                if (IsChunkValidSource(src, chunk)) {
                    chunk.Current.LastFrameDependency = group.RenderManager.DeviceManager.FrameIndex;
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

            if (RenderTrace.EnableTracing)
                layer++;
        }

        internal void MaybeSetLifeRampParameters (MaterialEffectParameters p) {
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
                    : Engine.DummyRampTexture;
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
            lock (Chunks)
            if (Chunks.Count == 0)
                return;

            if (Engine.ResetCount != LastResetCount)
                return;

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
                    material, blendState: blendState, depthStencilState: Configuration.DepthStencilState
                );

            // HACK: Fix an obscure crash where the last texture we used was disposed, since we won't update it
            //  until later in the draw process but FNA checks it when the material is applied instead of at draw
            foreach (var p in material.Parameters.AllParameters) {
                if (p.ParameterType == EffectParameterType.Texture2D)
                    p.SetValue(Engine.Coordinator.Manager.DummyTexture);
            }
            
            // HACK
            if (renderParams != null)
                renderParams.Material = material;
            using (var group = BatchGroup.New(
                container, layer,
                Renderer.BeforeDraw, Renderer.AfterDraw, 
                userData: (object)renderParams ?? (object)material
            )) {
                // HACK: This ensures our parameters get applied to the material, since groups normally don't
                //  have a material associated with them.
                group.Material = material;
                var m = material;

                // FIXME: We previously did a LastResetCount check in the setup handler. We might need to bring it back?

                // FIXME: deltaTime
                // SetSystemUniforms(m, 0);
                var tex = appearance?.Texture?.Instance;
                if ((tex != null) && tex.IsDisposed)
                    tex = null;
                var texSize = (tex != null)
                    ? new Vector2(tex.Width, tex.Height)
                    : Vector2.One;

                // TODO: transform arg
                group.MaterialParameters.Add("BitmapTexture", tex);
                if (tex != null) {
                    // var offset = new Vector2(-0.5f) / texSize;
                    var offset = appearance.OffsetPx / texSize;
                    var size = appearance.SizePx.GetValueOrDefault(texSize) / texSize;
                    group.MaterialParameters.Add("BitmapTextureRegion", new Vector4(
                        offset.X, offset.Y, 
                        offset.X + size.X, offset.Y + size.Y
                    ));
                }
                
                group.MaterialParameters.Add("StippleFactor", renderParams?.StippleFactor ?? Configuration.StippleFactor);

                var renderingOptions = new Vector4(
                    (appearance?.Rounded ?? false) ? 1 : 0,
                    (appearance?.DitheredOpacity ?? false) ? 1 : 0,
                    (appearance?.ColumnFromVelocity ?? false) ? 1 : 0,
                    (appearance?.RowFromVelocity ?? false) ? 1 : 0
                );

                group.MaterialParameters.Add("RenderingOptions", renderingOptions);

                int i = 1;
                lock (Chunks)
                foreach (var chunk in Chunks)
                    RenderChunk(group, chunk, material, i++, usePreviousData);
            }
        }

        internal void ResetInternalState () {
            // FIXME: Release buffers
            foreach (var chunk in Chunks) {
                if (chunk.Previous != null) {
                    chunk.Previous.CurrentOwnerID = 0;
                    chunk.Previous.CurrentOwnerFrameIndex = -1;
                    Engine.DiscardedBuffers.Add(chunk.Previous);
                }
                if (chunk.Current != null) {
                    chunk.Current.CurrentOwnerID = 0;
                    chunk.Current.CurrentOwnerFrameIndex = -1;
                    Engine.DiscardedBuffers.Add(chunk.Current);
                }
                Engine.Coordinator.DisposeResource(chunk);
            }
            LivenessInfos.Clear();
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            ResetInternalState();
            Engine.Systems.Remove(this);
        }

        void IParticleSystems.Update (IBatchContainer container, int layer) {
            Update(container, layer);
        }
    }
}