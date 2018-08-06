using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Util;
using Squared.Render;
using Chunk = Squared.Illuminant.Particles.ParticleSystem.Slice.Chunk;

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

        internal const int              RandomnessTextureWidth = 2048,
                                        RandomnessTextureHeight = 256;
        internal          Texture2D     RandomnessTexture;

        internal readonly List<Chunk> FreeList = 
            new List<Chunk>();

        private static readonly short[] TriIndices = new short[] {
            0, 1, 2
        };

        public ParticleEngine (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, ParticleEngineConfiguration configuration
        ) {
            Coordinator = coordinator;
            Materials = materials;

            ParticleMaterials = new ParticleMaterials(materials);
            Configuration = configuration;

            LoadMaterials(content);

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
                        buf[i].Offset = new Vector2(x / fsize, y / fsize);
                    }
                }

                RasterizeOffsetBuffer.SetData(buf);
            }
        }

        private void GenerateRandomnessTexture () {
            lock (Coordinator.CreateResourceLock) {
                // TODO: HalfVector4?
                RandomnessTexture = new Texture2D(
                    Coordinator.Device,
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

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            ResetCount += 1;
            // FillIndexBuffer();
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            lock (FreeList) {
                foreach (var c in FreeList)
                    Coordinator.DisposeResource(c);
                FreeList.Clear();
            }

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

        public int FreeListCapacity = 12;

        public ParticleEngineConfiguration (int chunkSize = 256) {
            ChunkSize = chunkSize;
        }
    }
}
