using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Illuminant.Configuration;
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Render.Evil;
using Squared.Render.Resources;
using Squared.Render.Tracing;
using Squared.Util;
using Squared.Util.Text;
using Chunk = Squared.Illuminant.Particles.ParticleSystem.Chunk;

namespace Squared.Illuminant.Particles {
    public partial class ParticleEngine : IDisposable {
        public const int MaxLivenessCheckChunkCount = 512;

        public bool IsDisposed { get; private set; }
        
        public readonly RenderCoordinator           Coordinator;

        public readonly DefaultMaterialSet          Materials;
        public readonly bool                        OwnsMaterials;
        public          ParticleMaterials           ParticleMaterials { get; private set; }

        public readonly ParticleEngineConfiguration Configuration;

        internal int ResetCount = 0;

        internal IndexBuffer   TriIndexBuffer;
        internal VertexBuffer  TriVertexBuffer;
        internal IndexBuffer   RasterizeIndexBuffer;
        internal VertexBuffer  RasterizeVertexBuffer;
        internal VertexBuffer  RasterizeOffsetBuffer;

        internal const int              RandomnessTextureWidth = 807,
                                        RandomnessTextureHeight = 653;
        internal          Texture2D     RandomnessTexture, 
            LowPrecisionRandomnessTexture,
            DummyRampTexture;

        internal         RenderTarget2D ScratchTexture;

        internal readonly List<ParticleSystem.BufferSet> AllBuffers = 
            new List<ParticleSystem.BufferSet>();
        internal readonly UnorderedList<ParticleSystem.BufferSet> AvailableBuffers
            = new UnorderedList<ParticleSystem.BufferSet>();
        internal readonly UnorderedList<ParticleSystem.BufferSet> DiscardedBuffers
            = new UnorderedList<ParticleSystem.BufferSet>();

        internal readonly HashSet<ParticleSystem> Systems = 
            new HashSet<ParticleSystem>(new ReferenceComparer<ParticleSystem>());

        private readonly EffectProvider Effects;
        private readonly Dictionary<Type, Delegate> GenericResolvers = 
            new Dictionary<Type, Delegate>(new ReferenceComparer<Type>());
        private readonly Dictionary<Type, Delegate> GenericBoxedResolvers = 
            new Dictionary<Type, Delegate>(new ReferenceComparer<Type>());

        internal readonly TypedUniform<Uniforms.ParticleSystem> uSystem;
        internal readonly TypedUniform<Uniforms.RasterizeParticleSystem> uRasterize;
        internal readonly TypedUniform<Uniforms.DistanceField> uDistanceField;
        internal readonly TypedUniform<Uniforms.ClampedBezier4> uColorFromLife, uColorFromVelocity;
        internal readonly TypedUniform<Uniforms.ClampedBezier1> uSizeFromLife, uSizeFromVelocity, uRoundingPowerFromLife;

        public readonly NamedConstantResolver<float>   ResolveSingle;
        public readonly NamedConstantResolver<Vector2> ResolveVector2;
        public readonly NamedConstantResolver<Vector3> ResolveVector3;
        public readonly NamedConstantResolver<Vector4> ResolveVector4;
        public readonly NamedConstantResolver<Matrix>  ResolveMatrix;
        public readonly NamedConstantResolver<DynamicMatrix> ResolveDynamicMatrix;

        private readonly RenderTargetRing LivenessQueryRTs;
        private bool IsLivenessQueryRequestPending = false;

        internal readonly HashSet<ParticleSystem> LivenessQueryRequests = new HashSet<ParticleSystem>();

        private Action IssueLivenessQueries;
        private VertexBufferBinding[] TwoBindings = new VertexBufferBinding[2],
            ThreeBindings = new VertexBufferBinding[3];

        private static readonly short[] TriIndices = new short[] {
            0, 1, 2
        };

        public ParticleEngine (
            RenderCoordinator coordinator, DefaultMaterialSet materials, 
            ParticleEngineConfiguration configuration, ParticleMaterials particleMaterials = null
        ) {
            Coordinator = coordinator;
            Materials = materials;
            IssueLivenessQueries = _IssueLivenessQueries;

            Effects = new EffectProvider(System.Reflection.Assembly.GetExecutingAssembly(), coordinator);

            OwnsMaterials = (particleMaterials == null);
            ParticleMaterials = particleMaterials ?? new ParticleMaterials(materials);
            Configuration = configuration;

            uSystem = materials.NewTypedUniform<Uniforms.ParticleSystem>("System");
            uRasterize = materials.NewTypedUniform<Uniforms.RasterizeParticleSystem>("RasterizeSettings");
            uDistanceField = materials.NewTypedUniform<Uniforms.DistanceField>("DistanceField");

            uColorFromLife = materials.NewTypedUniform<Uniforms.ClampedBezier4>("ColorFromLife");
            uColorFromVelocity = materials.NewTypedUniform<Uniforms.ClampedBezier4>("ColorFromVelocity");

            uSizeFromLife = materials.NewTypedUniform<Uniforms.ClampedBezier1>("SizeFromLife");
            uSizeFromVelocity = materials.NewTypedUniform<Uniforms.ClampedBezier1>("SizeFromVelocity");
            uRoundingPowerFromLife = materials.NewTypedUniform<Uniforms.ClampedBezier1>("RoundingPowerFromLife");

            LivenessQueryRTs = new RenderTargetRing(Coordinator, 4, MaxLivenessCheckChunkCount, 1, false, SurfaceFormat.Rg32, DepthFormat.Depth16, 1);

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            var resolveGeneric = GetType().GetMethod("ResolveGeneric", flags);
            foreach (var f in GetType().GetFields(flags)) {
                if (!f.Name.StartsWith("Resolve"))
                    continue;

                var valueType = f.FieldType.GetGenericArguments()[0];
                var delegateType = typeof(NamedConstantResolver<>).MakeGenericType(valueType);
                var typedResolveGeneric = resolveGeneric.MakeGenericMethod(valueType);
                var resolver = Delegate.CreateDelegate(delegateType, this, typedResolveGeneric, true);
                GenericResolvers[valueType] = resolver;
                f.SetValue(this, resolver);
            }

            LoadMaterials(Effects);

            CreateInternalState(Coordinator);

            Coordinator.DeviceReset += Coordinator_DeviceReset;
        }

        public long CurrentTurn { get; private set; }

        private void SiftBuffers (int frameIndex) {
            if (DiscardedBuffers.Count == 0)
                return;

            ParticleSystem.BufferSet b;
            using (var e = DiscardedBuffers.GetEnumerator())
            while (e.GetNext(out b)) {
                if (b.CurrentOwnerFrameIndex < frameIndex) {
                    b.CurrentOwnerID = 0;
                    b.CurrentOwnerFrameIndex = -1;
                }

                // We can't reuse any buffers that were recently used for painting or readback
                if (b.LastFrameDependency >= (frameIndex - Configuration.FrameDependencyLength))
                    continue;

                var age = CurrentTurn - b.LastTurnUsed;
                if (age >= Configuration.RecycleTurnInterval) {
                    e.RemoveCurrent();
                    if (!b.IsDisposed)
                        AvailableBuffers.Add(b);
                }
            }

            // Console.WriteLine($"{AvailableBuffers.Count} buffers available and {DiscardedBuffers.Count} unavailable after sift");
        }

        internal IParameter FindConstantBoxed (string name) {
            if (Configuration.NamedVariableResolver == null)
                return null;

            var gen = Configuration.NamedVariableResolver(name);
            return gen;
        }

        internal bool FindConstant<T> (string name, out Parameter<T> result)
            where T : struct {
            result = default(Parameter<T>);

            var gen = FindConstantBoxed(name);
            if (gen == null)
                return false;

            if (gen.ValueType == typeof(T)) {
                result = (Parameter<T>)gen;
                return true;
            } else {
                return false;
            }
        }

        public bool ResolveGeneric<T> (string name, float t, out T result)
            where T : struct {
            result = default(T);
            Parameter<T> constant;
            if (!FindConstant(name, out constant))
                return false;

            var resolver = GenericResolvers[typeof(T)];
            result = constant.Evaluate(t, (NamedConstantResolver<T>)resolver);
            return true;
        }

        public object ResolveBoxed (string name, float t) {
            var constant = FindConstantBoxed(name);
            if (constant == null)
                return null;

            var resolverType = constant.ValueType;
            var resolver = GenericResolvers[resolverType];
            return constant.EvaluateBoxed(t, resolver);
        }

        internal void NextTurn (int frameIndex) {
            CurrentTurn += 1;

            SiftBuffers(frameIndex);
        }

        private void ProcessLivenessInfoData (Rg32[] buffer, Chunk[] chunks) {
            // Console.WriteLine("Process liveness info data {0}", buffer);
            for (int i = 0; i < chunks.Length; i++) {
                var chunk = chunks[i];
                if (chunk == null) {
                    // Console.WriteLine("No chunk");
                    continue;
                }

                var li = chunk.System.GetLivenessInfo(chunk);
                if (li == null) {
                    // Console.WriteLine("No liveness info");
                    continue;
                }

                if (buffer == null) {
                    // Console.WriteLine("No buffer");
                    continue;
                }

                var raw = buffer[i].PackedValue;
                var flag = raw & 0xFFFF0000;
                var count = raw & 0xFFFF;
                li.Count = (int)count;
                // Console.WriteLine("#{0:0000} count = {1:00000}", chunk.ID, count);

                chunk.System.ProcessLatestLivenessInfo(chunk);
            }
        }

        private void PerformApproximateLivenessQueries () {
            lock (LivenessQueryRequests) {
                foreach (var system in LivenessQueryRequests) {
                    int count;
                    BufferPool<Chunk>.Buffer chunkList;

                    lock (system.Chunks) {
                        count = system.Chunks.Count;
                        chunkList = BufferPool<Chunk>.Allocate(count);
                        system.Chunks.CopyTo(chunkList.Data, 0);
                    }

                    using (var counts = Squared.Util.BufferPool<Rg32>.Allocate(chunkList.Data.Length)) {
                        for (int i = 0; i < count; i++)
                            counts.Data[i] = new Rg32(chunkList.Data[i].ApproximateMaximumLife > 0 ? 1 : 0, 0);

                        ProcessLivenessInfoData(counts.Data, chunkList.Data);
                    }

                    chunkList.Dispose();
                }

                LivenessQueryRequests.Clear();

                IsLivenessQueryRequestPending = false;
            }
        }

        private void _IssueLivenessQueries () {
            if (Configuration.ApproximateLivenessCounts) {
                PerformApproximateLivenessQueries();
                return;
            }

            Monitor.Enter(LivenessQueryRequests);
            IsLivenessQueryRequestPending = false;
            // Console.WriteLine("Issue {0} liveness queries", LivenessQueryRequests.Count);

            if (LivenessQueryRequests.Count > LivenessQueryRTs.Width) {
                Monitor.Exit(LivenessQueryRequests);
                throw new Exception("Too many liveness queries");
            }

            lock (Coordinator.UseResourceLock) {
                var wt = LivenessQueryRTs.AcquireWriteTarget();
                var nextBuffer = wt.Target;
                var previousBuffer = wt.Previous;

                var dm = Coordinator.Manager.DeviceManager;

                RenderTrace.ImmediateMarker(dm.Device, "Compute liveness for {0} system(s)", LivenessQueryRequests.Count);
                var m = Configuration.AccurateLivenessCounts
                    ? ParticleMaterials.CountLiveParticles
                    : ParticleMaterials.CountLiveParticlesFast;

                if (!AutoRenderTarget.IsRenderTargetValid(nextBuffer)) {
                    Monitor.Exit(LivenessQueryRequests);
                    return;
                }

                var p = m.Parameters;
                p["PositionTexture"].SetValue((Texture2D)null);

                dm.PushRenderTarget(nextBuffer);
                dm.Device.Clear(
                    ClearOptions.Target | ClearOptions.DepthBuffer, 
                    Color.Transparent, 0, 0
                );
                dm.ApplyMaterial(m);
                dm.Device.BlendState = BlendState.Additive;
                dm.Device.RasterizerState = RasterizerState.CullNone;
                dm.Device.DepthStencilState = (Configuration.AccurateLivenessCounts)
                    ? DepthStencilState.None
                    : ParticleMaterials.CountDepthStencilState;

                int i = 0;
                foreach (var system in LivenessQueryRequests) {
                    var chunkList = BufferPool<Chunk>.Allocate(system.Chunks.Count);
                    lock (system.Chunks) {
                        system.Chunks.CopyTo(chunkList.Data, 0);
                        var wi = new LivenessDataReadbackWorkItem {
                            RenderTarget = previousBuffer,
                            NeedResourceLock = true,
                            Engine = this,
                            Chunks = chunkList,
                            ResetCount = ResetCount
                        };
                        // HACK: Perform right before present to reduce the odds that this makes us miss a frame, 
                        //  since present blocks on vsync and so does a gpu readback in debug contexts
                        // FIXME: Remove this allocation
                        Coordinator.BeforePresent(() => wi.Execute(null));
                    }

                    RenderTrace.ImmediateMarker(dm.Device, "Compute liveness for {0} chunk(s)", chunkList.Data.Length);
                    foreach (var chunk in chunkList.Data) {
                        if (chunk.TotalSpawned == 0) {
                            i++;
                            continue;
                        }

                        var srcTexture = chunk.Current.PositionAndLife;
                        if (!AutoRenderTarget.IsRenderTargetValid(srcTexture)) {
                            i++;
                            continue;
                        }

                        system.SetSystemUniforms(m, 0);
                        p["ChunkIndexAndMaxIndex"].SetValue(new Vector2(i, LivenessQueryRTs.Width));
                        p["PositionTexture"].SetValue(srcTexture);
                        m.Flush(dm);

                        var call = new NativeDrawCall(
                            PrimitiveType.TriangleList,
                            RasterizeVertexBuffer, 0,
                            RasterizeOffsetBuffer, 0,
                            null, 0,
                            RasterizeIndexBuffer, 0, 0, 4, 0, 2,
                            chunk.TotalSpawned
                        );
                        NativeBatch.IssueDrawCall(
                            dm.Device, ref call,
                            TwoBindings, ThreeBindings
                        );
                        i++;
                    }
                }

                dm.PopRenderTarget();
                LivenessQueryRequests.Clear();

                Monitor.Exit(LivenessQueryRequests);
            }
        }

        private void AutoIssueLivenessQueries () {
            lock (LivenessQueryRequests) {
                if (LivenessQueryRequests.Count == 0)
                    return;

                if (IsLivenessQueryRequestPending)
                    return;

                IsLivenessQueryRequestPending = true;
            }

            Coordinator.BeforeIssue(IssueLivenessQueries);
        }

        internal void EndOfUpdate (IBatchContainer container, int layer, long initialTurn, int frameIndex) {
            AutoIssueLivenessQueries();
            SiftBuffers(frameIndex);

            ParticleSystem.BufferSet b;
            using (var e = AvailableBuffers.GetEnumerator())
            while (e.GetNext(out b)) {
                if (b.LastTurnUsed >= initialTurn)
                    continue;

                if (AvailableBuffers.Count > Configuration.SpareBufferCount) {
                    // Console.WriteLine("Discarding unused buffer " + b.ID);
                    Coordinator.DisposeResource(b);
                    AllBuffers.Remove(b);
                    e.RemoveCurrent();
                }
            }
        }

        public long EstimateMemoryUsage () {
            var ibSize = RasterizeIndexBuffer.IndexCount * 2;
            var obSize = RasterizeOffsetBuffer.VertexCount * Marshal.SizeOf(typeof(ParticleOffsetVertex));
            var vbSize = RasterizeVertexBuffer.VertexCount * Marshal.SizeOf(typeof(ParticleSystemVertex));

            // Color, RenderData and RenderColor
            long chunkTotal = 0;
            foreach (var s in Systems)
                foreach (var c in s.Chunks)
                    chunkTotal += (c.Size * c.Size * (4 * 4) * 3);

            // Position/Velocity buffers
            long bufTotal = 0;
            foreach (var buf in AllBuffers) {
                var bufSize = buf.Size * buf.Size * 2 * (4 * 4);
                bufTotal += bufSize;
            }

            return (ibSize + obSize + vbSize + chunkTotal + bufTotal);
        }

        private void FillIndexBuffer () {
            var buf = new short[] {
                0, 1, 3, 1, 2, 3
            };
            RasterizeIndexBuffer = new IndexBuffer(
                Coordinator.Device, IndexElementSize.SixteenBits, 
                buf.Length, BufferUsage.WriteOnly
            );
            RasterizeIndexBuffer.SetData(buf);
        }

        private void FillVertexBuffer () {
            var buf = new ParticleSystemVertex[4];
            buf[1].CornerWeights = new Vector3(1, 0, 0);
            buf[2].CornerWeights = new Vector3(1, 1, 0);
            buf[3].CornerWeights = new Vector3(0, 1, 0);

            RasterizeVertexBuffer = new VertexBuffer(
                Coordinator.Device, typeof(ParticleSystemVertex),
                buf.Length, BufferUsage.WriteOnly
            );
            RasterizeVertexBuffer.SetData(buf);

            if (RasterizeOffsetBuffer != null)
                Coordinator.DisposeResource(RasterizeOffsetBuffer);

            var buf2 = new ParticleOffsetVertex[Configuration.ChunkSize * Configuration.ChunkSize];
            RasterizeOffsetBuffer = new VertexBuffer(
                Coordinator.Device, typeof(ParticleOffsetVertex),
                buf2.Length, BufferUsage.WriteOnly
            );
            var fsize = (float)Configuration.ChunkSize;

            for (var y = 0; y < Configuration.ChunkSize; y++) {
                for (var x = 0; x < Configuration.ChunkSize; x++) {
                    var i2 = (y * Configuration.ChunkSize) + x;
                    buf2[i2].OffsetAndIndex = new Vector3(x / fsize, y / fsize, i2);
                }
            }

            RasterizeOffsetBuffer.SetData(buf2);
        }

        public void ChangePropertiesAndReset (int newSize) {
            Coordinator.WaitForActiveDraws();

            Configuration.ChunkSize = newSize;

            foreach (var s in Systems)
                s.Reset();

            ResetInternalState();
            CreateInternalState(Coordinator);
        }

        private void GenerateRandomnessTexture (int? seed = null) {
            var sw = Stopwatch.StartNew();
            lock (Coordinator.CreateResourceLock) {
                // TODO: HalfVector4?
                RandomnessTexture = new Texture2D(
                    Coordinator.Device,
                    RandomnessTextureWidth, RandomnessTextureHeight, false,
                    SurfaceFormat.Vector4
                ) {
                    Name = "ParticleEngine.RandomnessTexture",
                };
                // TODO: Mip chain?
                LowPrecisionRandomnessTexture = new Texture2D(
                    Coordinator.Device,
                    RandomnessTextureWidth, RandomnessTextureHeight, false,
                    SurfaceFormat.Rgba64
                ) {
                    Name = "ParticleEngine.LowPrecisionRandomnessTexture",
                };

                // FIXME: Use NativeAllocation for this to avoid fragmenting LOH
                var buffer = new Vector4[RandomnessTextureWidth * RandomnessTextureHeight];
                int o;
                unchecked {
                    o = seed.GetValueOrDefault((int)Time.Ticks);
                }

                Parallel.For(
                    0, RandomnessTextureHeight,
                    () => new CoreCLR.Xoshiro(null),
                    (y, pls, rng) => {
                        int j = y * RandomnessTextureWidth;
                        for (int x = 0; x < RandomnessTextureWidth; x++)
                            buffer[j + x] = new Vector4(rng.NextSingle(), rng.NextSingle(), rng.NextSingle(), rng.NextSingle());
                        return rng;
                    },
                    (rng) => { }
                );

                RandomnessTexture.SetData(buffer);

                var buffer2 = new Rgba64[RandomnessTextureWidth * RandomnessTextureHeight];
                for (int i = 0; i < buffer.Length; i++)
                    buffer2[i] = new Rgba64(buffer[i]);

                LowPrecisionRandomnessTexture.SetData(buffer2);
            }

            // Console.WriteLine(sw.ElapsedMilliseconds);
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            ResetCount += 1;
            foreach (var sys in Systems)
                sys.NotifyDeviceReset();
            ResetInternalState();
            CreateInternalState(Coordinator);
            // FillIndexBuffer();
        }

        private void CreateInternalState (RenderCoordinator coordinator) {
            lock (coordinator.CreateResourceLock) {
                TriIndexBuffer = new IndexBuffer(coordinator.Device, IndexElementSize.SixteenBits, 3, BufferUsage.WriteOnly);
                TriIndexBuffer.SetData(TriIndices);

                const float argh = 99999999;

                TriVertexBuffer = new VertexBuffer(coordinator.Device, typeof(ParticleSystemVertex), 3, BufferUsage.WriteOnly);
                TriVertexBuffer.SetData(new [] {
                    // HACK: Workaround for Intel's terrible video drivers.
                    // No, I don't know why.
                    new ParticleSystemVertex(-2, -2, 0),
                    new ParticleSystemVertex(argh, -2, 1),
                    new ParticleSystemVertex(-2, argh, 2),
                });

                ScratchTexture = new RenderTarget2D(
                    coordinator.Device, Configuration.ChunkSize, Configuration.ChunkSize,
                    false, SurfaceFormat.Alpha8, DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                ) {
                    Name = "ParticleEngine.ScratchTexture"
                };

                DummyRampTexture = new Texture2D(coordinator.Device, 1, 1, false, SurfaceFormat.Color);
                DummyRampTexture.SetData(new Color[1]);
            }

            FillIndexBuffer();
            FillVertexBuffer();
            GenerateRandomnessTexture();
        }

        private void ResetInternalState () {
            foreach (var sys in Systems.ToArray())
                sys.ResetInternalState();

            foreach (var buf in AllBuffers.ToArray())
                Coordinator.DisposeResource(buf);

            AllBuffers.Clear();
            AvailableBuffers.Clear();
            DiscardedBuffers.Clear();

            Coordinator.DisposeResource(DummyRampTexture);
            Coordinator.DisposeResource(TriIndexBuffer);
            Coordinator.DisposeResource(TriVertexBuffer);
            Coordinator.DisposeResource(RasterizeIndexBuffer);
            Coordinator.DisposeResource(RasterizeVertexBuffer);
            Coordinator.DisposeResource(RasterizeOffsetBuffer);
            Coordinator.DisposeResource(RandomnessTexture);
            Coordinator.DisposeResource(LowPrecisionRandomnessTexture);
            Coordinator.DisposeResource(LivenessQueryRTs);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            ResetInternalState();
            // FIXME: We leak the effects provider here
            if (OwnsMaterials)
                Effects.Dispose();
        }
    }

    public class ParticleEngineConfiguration {
        public int ChunkSize { get; internal set; }

        /// <summary>
        /// How long a buffer must remain unused before getting used again.
        /// Lower values reduce memory usage but can cause performance or correctness issues.
        /// Increasing the recycle interval allows the GPU more time to render to a buffer before we reuse it.
        /// </summary>
        public int RecycleTurnInterval = 2;

        public int FrameDependencyLength = 1;

        /// <summary>
        /// The maximum number of spare buffers to keep around.
        /// </summary>
        public int SpareBufferCount = 20;

        /// <summary>
        /// Used to measure elapsed time automatically for updates
        /// </summary>
        public ITimeProvider TimeProvider = null;

        /// <summary>
        /// If set, updates occur at a fixed time step for consistent behavior.
        /// A single Update call will still only perform one update pass, but the elapsed time
        ///  will be an integer multiple of the time step.
        /// </summary>
        public int? UpdatesPerSecond = null;

        /// <summary>
        /// Any update's elapsed time will be limited to at most this long
        /// </summary>
        public double MaximumUpdateDeltaTimeSeconds = 1.0 / 20;

        /// <summary>
        /// Used to load lazy texture resources.
        /// </summary>
        public Func<AbstractString, Texture2D> TextureLoader = null;

        /// <summary>
        /// Used to load lazy texture resources in floating-point format.
        /// </summary>
        public Func<AbstractString, Texture2D> FPTextureLoader = null;

        /// <summary>
        /// Used to resolve ParticleSystemReferences for feedback.
        /// </summary>
        public Func<string, int?, ParticleSystem> SystemResolver = null;

        /// <summary>
        /// Used to resolve named constants referenced by parameters.
        /// </summary>
        public Func<string, IParameter> NamedVariableResolver = null;

        /// <summary>
        /// Enables accurate counting of the number of live particles in a given chunk.
        /// If disabled, only minimal tracking will be performed to identify dead chunks.
        /// </summary>
        public bool AccurateLivenessCounts {
            get {
                return _AccurateLivenessCounts && !ApproximateLivenessCounts;
            }
            set {
                _AccurateLivenessCounts = value;
            }
        }

        private bool _AccurateLivenessCounts = true;

        /// <summary>
        /// Estimates (worst-case) particle liveness on the CPU instead of doing counts
        ///  on the GPU. This can result in chunks staying alive longer but works around
        ///  the reality that OpenGL sucks and liveness counts are very slow.
        /// This is faster than the readback in Direct3D too anyway so whatever. Why not.
        /// </summary>
        public bool ApproximateLivenessCounts = true;

        public ParticleEngineConfiguration (int chunkSize = 256) {
            ChunkSize = chunkSize;
        }
    }
}
