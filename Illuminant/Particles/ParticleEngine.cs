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
using Squared.Render.Tracing;
using Squared.Util;
using Chunk = Squared.Illuminant.Particles.ParticleSystem.Chunk;

namespace Squared.Illuminant.Particles {
    public partial class ParticleEngine : IDisposable {
        public const int MaxLivenessCheckChunkCount = 512;

        public bool IsDisposed { get; private set; }
        
        public readonly RenderCoordinator           Coordinator;

        public readonly DefaultMaterialSet          Materials;
        public          ParticleMaterials           ParticleMaterials { get; private set; }

        public readonly ParticleEngineConfiguration Configuration;

        internal int ResetCount = 0;

        internal readonly IndexBuffer   TriIndexBuffer;
        internal readonly VertexBuffer  TriVertexBuffer;
        internal          IndexBuffer   RasterizeIndexBuffer;
        internal          VertexBuffer  RasterizeVertexBuffer;
        internal          VertexBuffer  RasterizeOffsetBuffer;

        internal const int              RandomnessTextureWidth = 807,
                                        RandomnessTextureHeight = 653;
        internal          Texture2D     RandomnessTexture, 
            LowPrecisionRandomnessTexture;

        internal         RenderTarget2D ScratchTexture;

        internal readonly List<ParticleSystem.BufferSet> AllBuffers = 
            new List<ParticleSystem.BufferSet>();
        internal readonly UnorderedList<ParticleSystem.BufferSet> AvailableBuffers
            = new UnorderedList<ParticleSystem.BufferSet>();
        internal readonly UnorderedList<ParticleSystem.BufferSet> DiscardedBuffers
            = new UnorderedList<ParticleSystem.BufferSet>();

        internal readonly HashSet<ParticleSystem> Systems = 
            new HashSet<ParticleSystem>(new ReferenceComparer<ParticleSystem>());

        private readonly EmbeddedEffectProvider Effects;
        private readonly Dictionary<Type, Delegate> GenericResolvers = 
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

        private Chunk[] LastLivenessInfoChunks = new Chunk[MaxLivenessCheckChunkCount];
        private readonly RenderTargetRing LivenessQueryRTs;
        private bool IsLivenessQueryRequestPending = false;

        internal readonly HashSet<ParticleSystem> LivenessQueryRequests = new HashSet<ParticleSystem>();

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

            Effects = new EmbeddedEffectProvider(coordinator);

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
                    coordinator.Device, configuration.ChunkSize, configuration.ChunkSize, 
                    false, SurfaceFormat.Alpha8, DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                );
            }

            FillIndexBuffer();
            FillVertexBuffer();
            GenerateRandomnessTexture();

            Coordinator.DeviceReset += Coordinator_DeviceReset;
        }

        public long CurrentTurn { get; private set; }

        private void SiftBuffers (int frameIndex) {
            ParticleSystem.BufferSet b;
            using (var e = DiscardedBuffers.GetEnumerator())
            while (e.GetNext(out b)) {
                // We can't reuse any buffers that were recently used for painting or readback
                if (b.LastFrameDependency >= (frameIndex - 1))
                    continue;

                var age = CurrentTurn - b.LastTurnUsed;
                if (age >= Configuration.RecycleInterval) {
                    e.RemoveCurrent();
                    AvailableBuffers.Add(b);
                }
            }
        }

        internal bool FindConstant<T> (string name, out Parameter<T> result)
            where T : struct {
            result = default(Parameter<T>);
            if (Configuration.NamedVariableResolver == null)
                return false;

            var gen = Configuration.NamedVariableResolver(name);
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

        internal void NextTurn (int frameIndex) {
            CurrentTurn += 1;

            SiftBuffers(frameIndex);
        }

#if FNA
        private class LivenessDataReadbackWorkItem : Threading.IMainThreadWorkItem {
#else
        private struct LivenessDataReadbackWorkItem : Threading.IWorkItem {
#endif
            public RenderTarget2D RenderTarget;
            public ParticleEngine Engine;
            public bool NeedResourceLock;

            public void Execute () {
                if (!AutoRenderTargetBase.IsRenderTargetValid(RenderTarget))
                    return;

                // Console.WriteLine("Read liveness data texture");

                var startedWhen = Time.Ticks;
                using (var buffer = Squared.Util.BufferPool<Rg32>.Allocate(RenderTarget.Width)) {
                    if (NeedResourceLock)
                        Monitor.Enter(Engine.Coordinator.UseResourceLock);
                    try {
                        RenderTrace.ImmediateMarker("Read liveness data from previous frame");
                        RenderTarget.GetDataFast(buffer.Data);
                        var elapsedMs = (Time.Ticks - startedWhen) / (double)Time.MillisecondInTicks;
                        RenderTrace.ImmediateMarker("Readback took {0:000.0}ms", elapsedMs);
                    } finally {
                        if (NeedResourceLock)
                            Monitor.Exit(Engine.Coordinator.UseResourceLock);
                    }

                    Engine.ProcessLivenessInfoData(buffer.Data);
                }

            }
        }

        private void ProcessLivenessInfoData (Rg32[] buffer) {
            // Console.WriteLine("Process liveness info data {0}", buffer);
            lock (LastLivenessInfoChunks) {
                for (int i = 0; i < LastLivenessInfoChunks.Length; i++) {
                    var chunk = LastLivenessInfoChunks[i];
                    if (chunk == null)
                        continue;

                    var li = chunk.System.GetLivenessInfo(chunk);
                    if (li == null) 
                        continue;

                    if (buffer == null)
                        continue;

                    var raw = buffer[i].PackedValue;
                    var flag = raw & 0xFFFF0000;
                    var count = raw & 0xFFFF;
                    li.Count = (int)count;
                    // Console.WriteLine("#{0:0000} count = {1:00000}", chunk.ID, count);

                    chunk.System.IsLivenessInfoUpdated = true;
                }

                Array.Clear(LastLivenessInfoChunks, 0, LastLivenessInfoChunks.Length);
            }
        }

        private void IssueLivenessQueries () {
            Monitor.Enter(LivenessQueryRequests);
            IsLivenessQueryRequestPending = false;
            // Console.WriteLine("Issue {0} liveness queries", LivenessQueryRequests.Count);

            if (LivenessQueryRequests.Count > LivenessQueryRTs.Width)
                throw new Exception("Too many liveness queries");

            var wt = LivenessQueryRTs.AcquireWriteTarget();
            var nextBuffer = wt.Target;
            var previousBuffer = wt.Previous;

            var wi = new LivenessDataReadbackWorkItem {
                RenderTarget = previousBuffer,
                NeedResourceLock = true,
                Engine = this
            };
            // HACK: Perform right before present to reduce the odds that this makes us miss a frame, 
            //  since present blocks on vsync and so does a gpu readback in debug contexts
            Coordinator.BeforePresent(wi.Execute);

            RenderTrace.ImmediateMarker("Compute liveness for {0} system(s)", LivenessQueryRequests.Count);
            var m = Configuration.AccurateLivenessCounts
                ? ParticleMaterials.CountLiveParticles
                : ParticleMaterials.CountLiveParticlesFast;

            if (!AutoRenderTarget.IsRenderTargetValid(nextBuffer))
                return;

            var dm = Coordinator.Manager.DeviceManager;
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

            lock (LastLivenessInfoChunks) {
                Array.Clear(LastLivenessInfoChunks, 0, LastLivenessInfoChunks.Length);
                int i = 0;
                foreach (var system in LivenessQueryRequests) {
                    lock (system.Chunks) {
                        RenderTrace.ImmediateMarker("Compute liveness for {0} chunk(s)", system.Chunks.Count);
                        foreach (var chunk in system.Chunks) {
                            LastLivenessInfoChunks[i] = chunk;
                            if (chunk.TotalSpawned == 0) {
                                i++;
                                continue;
                            }

                            var srcTexture = chunk.LifeReadTexture;
                            if (!AutoRenderTarget.IsRenderTargetValid(srcTexture)) {
                                i++;
                                continue;
                            }

                            system.SetSystemUniforms(m, 0);
                            var p = m.Effect.Parameters;
                            p["ChunkIndexAndMaxIndex"].SetValue(new Vector2(i, LivenessQueryRTs.Width));
                            p["PositionTexture"].SetValue(srcTexture);
                            m.Flush();

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
                }
            }

            dm.PopRenderTarget();

            Monitor.Exit(LivenessQueryRequests);
        }

        private void AutoIssueLivenessQueries () {
            lock (LivenessQueryRequests)
                if (LivenessQueryRequests.Count == 0)
                    return;

            if (IsLivenessQueryRequestPending)
                return;

            IsLivenessQueryRequestPending = true;
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
            if (RasterizeVertexBuffer == null)
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
                    Coordinator.Device, typeof(ParticleSystemVertex),
                    buf.Length, BufferUsage.WriteOnly
                );
                RasterizeVertexBuffer.SetData(buf);
            }

            if (RasterizeOffsetBuffer != null)
                Coordinator.DisposeResource(RasterizeOffsetBuffer);

            {
                var buf = new ParticleOffsetVertex[Configuration.ChunkSize * Configuration.ChunkSize];
                RasterizeOffsetBuffer = new VertexBuffer(
                    Coordinator.Device, typeof(ParticleOffsetVertex),
                    buf.Length, BufferUsage.WriteOnly
                );
                var fsize = (float)Configuration.ChunkSize;

                for (var y = 0; y < Configuration.ChunkSize; y++) {
                    for (var x = 0; x < Configuration.ChunkSize; x++) {
                        var i = (y * Configuration.ChunkSize) + x;
                        buf[i].OffsetAndIndex = new Vector3(x / fsize, y / fsize, i);
                    }
                }

                RasterizeOffsetBuffer.SetData(buf);
            }
        }

        public void ChangePropertiesAndReset (int newSize) {
            Coordinator.WaitForActiveDraws();

            Configuration.ChunkSize = newSize;
            foreach (var s in Systems)
                s.Reset();

            foreach (var buf in AllBuffers)
                Coordinator.DisposeResource(buf);

            AllBuffers.Clear();
            AvailableBuffers.Clear();
            DiscardedBuffers.Clear();

            FillVertexBuffer();
        }

        private void GenerateRandomnessTexture (int? seed = null) {
            var sw = Stopwatch.StartNew();
            lock (Coordinator.CreateResourceLock) {
                // TODO: HalfVector4?
                RandomnessTexture = new Texture2D(
                    Coordinator.Device,
                    RandomnessTextureWidth, RandomnessTextureHeight, false,
                    SurfaceFormat.Vector4
                );
                // TODO: Mip chain?
                LowPrecisionRandomnessTexture = new Texture2D(
                    Coordinator.Device,
                    RandomnessTextureWidth, RandomnessTextureHeight, false,
                    SurfaceFormat.Rgba64
                );

                var buffer = new Vector4[RandomnessTextureWidth * RandomnessTextureHeight];
                int o;
                unchecked {
                    o = seed.GetValueOrDefault((int)Time.Ticks);
                }

                Parallel.For(
                    0, RandomnessTextureHeight,
                    () => {
                        return new MersenneTwister(Interlocked.Increment(ref o));
                    },
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
            // FillIndexBuffer();
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            foreach (var sys in Systems.ToArray())
                sys.Dispose();

            foreach (var buf in AllBuffers.ToArray())
                Coordinator.DisposeResource(buf);

            AllBuffers.Clear();
            AvailableBuffers.Clear();
            DiscardedBuffers.Clear();

            Effects.Dispose();

            Coordinator.DisposeResource(TriIndexBuffer);
            Coordinator.DisposeResource(TriVertexBuffer);
            Coordinator.DisposeResource(RasterizeIndexBuffer);
            Coordinator.DisposeResource(RasterizeVertexBuffer);
            Coordinator.DisposeResource(RasterizeOffsetBuffer);
            Coordinator.DisposeResource(RandomnessTexture);
            Coordinator.DisposeResource(LivenessQueryRTs);
        }
    }

    public class ParticleEngineConfiguration {
        public int ChunkSize { get; internal set; }

        /// <summary>
        /// How long a buffer must remain unused before getting used again.
        /// Lower values reduce memory usage but can cause performance or correctness issues.
        /// </summary>
        public int RecycleInterval = 4;

        /// <summary>
        /// The maximum number of spare buffers to keep around.
        /// </summary>
        public int SpareBufferCount = 16;

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
        public Func<string, Texture2D> TextureLoader = null;

        /// <summary>
        /// Used to load lazy texture resources in floating-point format.
        /// </summary>
        public Func<string, Texture2D> FPTextureLoader = null;

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
        public bool AccurateLivenessCounts = true;

        public ParticleEngineConfiguration (int chunkSize = 256) {
            ChunkSize = chunkSize;
        }
    }
}
