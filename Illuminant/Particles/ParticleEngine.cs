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
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Util;
using Chunk = Squared.Illuminant.Particles.ParticleSystem.Chunk;

namespace Squared.Illuminant.Particles {
    public partial class ParticleEngine : IDisposable {
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
                                        RandomnessTextureHeight = 381;
        internal          Texture2D     RandomnessTexture, 
            LowPrecisionRandomnessTexture;

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

        public readonly Configuration.NamedConstantResolver<float>   ResolveSingle;
        public readonly Configuration.NamedConstantResolver<Vector2> ResolveVector2;
        public readonly Configuration.NamedConstantResolver<Vector3> ResolveVector3;
        public readonly Configuration.NamedConstantResolver<Vector4> ResolveVector4;
        public readonly Configuration.NamedConstantResolver<Matrix>  ResolveMatrix;
        public readonly Configuration.NamedConstantResolver<Configuration.DynamicMatrix> ResolveDynamicMatrix;

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

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            var resolveGeneric = GetType().GetMethod("ResolveGeneric", flags);
            foreach (var f in GetType().GetFields(flags)) {
                if (!f.Name.StartsWith("Resolve"))
                    continue;

                var valueType = f.FieldType.GetGenericArguments()[0];
                var delegateType = typeof(Configuration.NamedConstantResolver<>).MakeGenericType(valueType);
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
            }

            FillIndexBuffer();
            FillVertexBuffer();
            GenerateRandomnessTexture();

            Coordinator.DeviceReset += Coordinator_DeviceReset;
        }

        public long CurrentTurn { get; private set; }

        private void SiftBuffers () {
            ParticleSystem.BufferSet b;
            using (var e = DiscardedBuffers.GetEnumerator())
            while (e.GetNext(out b)) {
                if (b.IsUpdateResult)
                    continue;
                var age = CurrentTurn - b.LastTurnUsed;
                if (age >= Configuration.RecycleInterval) {
                    e.RemoveCurrent();
                    AvailableBuffers.Add(b);
                }
            }
        }

        internal bool FindConstant<T> (string name, out Configuration.Parameter<T> result)
            where T : struct {
            result = default(Configuration.Parameter<T>);
            if (Configuration.NamedVariableResolver == null)
                return false;

            var gen = Configuration.NamedVariableResolver(name);
            if (gen == null)
                return false;

            if (gen.ValueType == typeof(T)) {
                result = (Configuration.Parameter<T>)gen;
                return true;
            } else {
                return false;
            }
        }

        public bool ResolveGeneric<T> (string name, float t, out T result)
            where T : struct {
            result = default(T);
            Configuration.Parameter<T> constant;
            if (!FindConstant(name, out constant))
                return false;

            var resolver = GenericResolvers[typeof(T)];
            result = constant.Evaluate(t, (Configuration.NamedConstantResolver<T>)resolver);
            return true;
        }

        internal void NextTurn () {
            CurrentTurn += 1;

            SiftBuffers();
        }

        internal void EndOfUpdate (long initialTurn) {
            SiftBuffers();

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

            // Attributes, RenderData and RenderColor
            long chunkTotal = 0;
            foreach (var s in Systems)
                foreach (var c in s.Chunks)
                    chunkTotal += (c.Size * c.Size * (Configuration.HighPrecision ? 4 * 4 : 2 * 4) * 3);

            // Position/Velocity buffers
            long bufTotal = 0;
            foreach (var buf in AllBuffers) {
                var bufSize = buf.Size * buf.Size * 2 * (Configuration.HighPrecision ? 4 * 4 : 2 * 4);
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
                    SurfaceFormat.Color
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

                var buffer2 = new Color[RandomnessTextureWidth * RandomnessTextureHeight];
                for (int i = 0; i < buffer.Length; i++)
                    buffer2[i] = new Color(buffer[i]);

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

            foreach (var sys in Systems)
                sys.Dispose();

            foreach (var buf in AllBuffers)
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
        }
    }

    public class ParticleEngineConfiguration {
        public readonly int ChunkSize;

        /// <summary>
        /// Store system state as 32-bit float instead of 16-bit float
        /// </summary>
        public bool HighPrecision = true;

        /// <summary>
        /// How long a buffer must remain unused before getting used again.
        /// Lower values reduce memory usage but can cause performance or correctness issues.
        /// </summary>
        public int RecycleInterval = 4;

        /// <summary>
        /// The maximum number of spare buffers to keep around.
        /// </summary>
        public int SpareBufferCount = 24;

        /// <summary>
        /// Used to measure elapsed time automatically for updates
        /// </summary>
        public ITimeProvider TimeProvider = null;

        /// <summary>
        /// Any update's elapsed time will be limited to at most this long
        /// </summary>
        public float MaximumUpdateDeltaTimeSeconds = 1 / 20f;

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
        public Func<string, Configuration.IParameter> NamedVariableResolver = null;

        public ParticleEngineConfiguration (int chunkSize = 256) {
            ChunkSize = chunkSize;
        }
    }
}
