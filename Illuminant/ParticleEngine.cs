﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Util;
using Squared.Render;
using Chunk = Squared.Illuminant.ParticleSystem.Slice.Chunk;

namespace Squared.Illuminant {
    public partial class ParticleEngine : IDisposable {
        public bool IsDisposed { get; private set; }
        
        public readonly RenderCoordinator           Coordinator;

        public readonly DefaultMaterialSet          Materials;
        public          ParticleMaterials           ParticleMaterials { get; private set; }

        public readonly ParticleEngineConfiguration Configuration;

        internal int ResetCount = 0;

        internal readonly IndexBuffer   QuadIndexBuffer;
        internal readonly VertexBuffer  QuadVertexBuffer;
        internal          IndexBuffer   RasterizeIndexBuffer;
        internal          VertexBuffer  RasterizeVertexBuffer;
        internal          VertexBuffer  RasterizeOffsetBuffer;

        internal const int              RandomnessTextureWidth = 2048,
                                        RandomnessTextureHeight = 256;
        internal          Texture2D     RandomnessTexture;

        private static readonly short[] QuadIndices = new short[] {
            0, 1, 3, 1, 2, 3
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
                QuadIndexBuffer = new IndexBuffer(coordinator.Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
                QuadIndexBuffer.SetData(QuadIndices);

                const float argh = 102400;

                QuadVertexBuffer = new VertexBuffer(coordinator.Device, typeof(ParticleSystemVertex), 4, BufferUsage.WriteOnly);
                QuadVertexBuffer.SetData(new [] {
                    // HACK: Workaround for Intel's terrible video drivers.
                    // No, I don't know why.
                    new ParticleSystemVertex(-argh, -argh, 0),
                    new ParticleSystemVertex(argh, -argh, 1),
                    new ParticleSystemVertex(argh, argh, 2),
                    new ParticleSystemVertex(-argh, argh, 3)
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
                var buf = new ParticleOffsetVertex[Chunk.MaximumCount];
                RasterizeOffsetBuffer = new VertexBuffer(
                    Coordinator.Device, typeof(ParticleOffsetVertex),
                    buf.Length, BufferUsage.WriteOnly
                );

                for (var y = 0; y < Chunk.Height; y++) {
                    for (var x = 0; x < Chunk.Width; x++) {
                        var i = (y * Chunk.Width) + x;
                        buf[i].Offset = new Vector2(x / (float)Chunk.Width, y / (float)Chunk.Height);
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

            Coordinator.DisposeResource(QuadIndexBuffer);
            Coordinator.DisposeResource(QuadVertexBuffer);
            Coordinator.DisposeResource(RasterizeIndexBuffer);
            Coordinator.DisposeResource(RasterizeVertexBuffer);
            Coordinator.DisposeResource(RasterizeOffsetBuffer);
            Coordinator.DisposeResource(RandomnessTexture);

            IsDisposed = true;            
        }
    }

    public class ParticleEngineConfiguration {
    }
}