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
        internal          Texture2D     RandomnessTexture;

        internal readonly List<ParticleSystem.BufferSet> AllBuffers = 
            new List<ParticleSystem.BufferSet>();
        internal readonly UnorderedList<ParticleSystem.BufferSet> AvailableBuffers
            = new UnorderedList<ParticleSystem.BufferSet>();
        internal readonly UnorderedList<ParticleSystem.BufferSet> DiscardedBuffers
            = new UnorderedList<ParticleSystem.BufferSet>();

        internal readonly HashSet<ParticleSystem> Systems = 
            new HashSet<ParticleSystem>(new ReferenceComparer<ParticleSystem>());

        private readonly EmbeddedEffectProvider Effects;

        private static readonly short[] TriIndices = new short[] {
            0, 1, 2
        };

        public ParticleEngine (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, ParticleEngineConfiguration configuration,
            ParticleMaterials particleMaterials = null
        ) {
            Coordinator = coordinator;
            Materials = materials;

            Effects = new EmbeddedEffectProvider(coordinator);

            ParticleMaterials = particleMaterials ?? new ParticleMaterials(materials);
            Configuration = configuration;

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
                var age = CurrentTurn - b.LastTurnUsed;
                if (age >= Configuration.RecycleInterval) {
                    e.RemoveCurrent();
                    AvailableBuffers.Add(b);
                }
            }
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

            // RenderData and RenderColor
            long chunkTotal = 0;
            foreach (var s in Systems)
                foreach (var c in s.Chunks)
                    chunkTotal += (c.Size * c.Size * (Configuration.HighPrecision ? 4 * 4 : 2 * 4) * 2);

            // Position/Velocity/Attributes buffers
            long bufTotal = 0;
            foreach (var buf in AllBuffers) {
                var bufSize = buf.Size * buf.Size * (buf.Attributes != null ? 3 : 2) * (Configuration.HighPrecision ? 4 * 4 : 2 * 4);
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
        public int RecycleInterval = 2;

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

        public ParticleEngineConfiguration (int chunkSize = 256) {
            ChunkSize = chunkSize;
        }
    }
}
