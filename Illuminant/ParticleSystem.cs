using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;

namespace Squared.Illuminant {
    public class ParticleSystem : IDisposable {
        private class Slice : IDisposable {
            public readonly int Index;
            public long Timestamp;
            public bool IsValid, IsBeingGenerated;
            public int  InUseCount;
            public RenderTarget2D PositionAndBirthTime;
            public RenderTarget2D Velocity;

            public Slice (
                GraphicsDevice device, int index, int columnCount, int rowCount,
                bool hackPopulate
            ) {
                Index = index;
                PositionAndBirthTime = new RenderTarget2D(
                    device,
                    columnCount, rowCount, false,
                    SurfaceFormat.Vector4, DepthFormat.None,
                    0, RenderTargetUsage.PreserveContents
                );
                Velocity = new RenderTarget2D(
                    device,
                    columnCount, rowCount, false,
                    SurfaceFormat.Vector4, DepthFormat.None,
                    0, RenderTargetUsage.PreserveContents
                );
                Timestamp = Util.Time.Ticks;

                if (hackPopulate) {
                    var seconds = (float)Util.Time.Seconds;

                    // HACK
                    var rng = new MersenneTwister(0);
                    var buf = new Vector4[columnCount * rowCount];

                    for (var i = 0; i < buf.Length; i++) {
                        var a = rng.NextDouble(0, Math.PI * 2);
                        var x = Math.Sin(a);
                        var y = Math.Cos(a);
                        var r = rng.NextDouble() * 200;

                        buf[i] = new Vector4(
                            (float)(600 + (r * x)),
                            (float)(600 + (r * y)),
                            0,
                            seconds
                        );
                    }

                    PositionAndBirthTime.SetData(buf);

                    var maxSpeed = 0.05f;

                    for (var i = 0; i < buf.Length; i++)
                        buf[i] = new Vector4(
                            rng.NextFloat(-1, 1) * maxSpeed,
                            rng.NextFloat(-1, 1) * maxSpeed,
                            0, 0
                        );

                    Velocity.SetData(buf);

                    IsValid = true;
                }
            }

            public void Dispose () {
                IsValid = false;
                PositionAndBirthTime.Dispose();
                Velocity.Dispose();
            }
        }

        public readonly int RowCount;
        public          int LiveCount { get; private set; }

        public readonly ParticleEngine              Engine;
        public readonly ParticleSystemConfiguration Configuration;

        private const int SliceCount          = 3;
        private const int RasterChunkRowCount = 24;
        private readonly int[] DeadCountPerRow;

        private Slice[] Slices;
        private RenderTarget2D 
            // Used to locate empty slots to spawn new particles into
            // We generate this every frame from a pass over the position buffer
            ParticleAgeFractionBuffer,
            // We generate this every frame from a pass over the age fraction buffer
            LiveParticleCountBuffer;

        private readonly IndexBuffer  QuadIndexBuffer;
        private readonly VertexBuffer QuadVertexBuffer;
        private          IndexBuffer  RasterizeIndexBuffer;
        private          VertexBuffer RasterizeVertexBuffer;

        private static readonly short[] QuadIndices = new short[] {
            0, 1, 3, 1, 2, 3
        };

        public ParticleSystem (
            ParticleEngine engine, ParticleSystemConfiguration configuration
        ) {
            Engine = engine;
            Configuration = configuration;
            RowCount = (Configuration.MaximumCount + Configuration.ParticlesPerRow - 1) / Configuration.ParticlesPerRow;
            DeadCountPerRow = new int[RowCount];
            LiveCount = 0;

            lock (engine.Coordinator.CreateResourceLock) {
                Slices = AllocateSlices();

                // TODO: Bitpack?
                ParticleAgeFractionBuffer = new RenderTarget2D(
                    engine.Coordinator.Device,
                    Configuration.ParticlesPerRow, RowCount, false,                    
                    SurfaceFormat.Alpha8, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );

                if (Configuration.ParticlesPerRow >= 255)
                    throw new Exception("Live count per row is packed into 8 bits");
                LiveParticleCountBuffer = new RenderTarget2D(
                    engine.Coordinator.Device,
                    RowCount, 1, false,                    
                    SurfaceFormat.Alpha8, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );

                QuadIndexBuffer = new IndexBuffer(engine.Coordinator.Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
                QuadIndexBuffer.SetData(QuadIndices);

                QuadVertexBuffer = new VertexBuffer(engine.Coordinator.Device, typeof(ParticleSystemVertex), 4, BufferUsage.WriteOnly);
                QuadVertexBuffer.SetData(new [] {
                    new ParticleSystemVertex(0, 0, 0),
                    new ParticleSystemVertex(1, 0, 1),
                    new ParticleSystemVertex(1, 1, 2),
                    new ParticleSystemVertex(0, 1, 3)
                });

                FillIndexBuffer();
                FillVertexBuffer();
            }
        }

        private void FillIndexBuffer () {
            var buf = new short[RasterChunkRowCount * Configuration.ParticlesPerRow * 6];
            int i = 0, j = 0;
            while (i < buf.Length) {
                buf[i++] = (short)(j + 0);
                buf[i++] = (short)(j + 1);
                buf[i++] = (short)(j + 3);
                buf[i++] = (short)(j + 1);
                buf[i++] = (short)(j + 2);
                buf[i++] = (short)(j + 3);

                j += 4;
            }

            RasterizeIndexBuffer = new IndexBuffer(
                Engine.Coordinator.Device, IndexElementSize.SixteenBits, 
                buf.Length, BufferUsage.WriteOnly
            );
            RasterizeIndexBuffer.SetData(buf);
        }

        private void FillVertexBuffer () {
            var buf = new ParticleSystemVertex[RasterChunkRowCount * Configuration.ParticlesPerRow * 4];
            int i = 0;
            for (var y = 0; y < RasterChunkRowCount; y++) {
                for (var x = 0; x < Configuration.ParticlesPerRow; x++) {
                    var v = new ParticleSystemVertex(
                        x / (float)Configuration.ParticlesPerRow,
                        y / (float)RowCount, 0
                    );
                    buf[i++] = v;
                    v.Corner = v.Unused = 1;
                    buf[i++] = v;
                    v.Corner = v.Unused = 2;
                    buf[i++] = v;
                    v.Corner = v.Unused = 3;
                    buf[i++] = v;
                }
            }

            RasterizeVertexBuffer = new VertexBuffer(
                Engine.Coordinator.Device, typeof(ParticleSystemVertex),
                buf.Length, BufferUsage.WriteOnly
            );
            RasterizeVertexBuffer.SetData(buf);
        }

        private Slice[] AllocateSlices () {
            var result = new Slice[SliceCount];
            for (var i = 0; i < result.Length; i++)
                result[i] = new Slice(Engine.Coordinator.Device, i, Configuration.ParticlesPerRow, RowCount, i == 0);

            return result;
        }

        public void Update (IBatchContainer container, int layer) {
            Slice source, dest;

            lock (Slices) {
                source = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).First();

                lock (source)
                    source.InUseCount++;
            }

            lock (Slices) {
                dest = (
                    from s in Slices where (!s.IsBeingGenerated && s.InUseCount <= 0)
                    orderby s.Timestamp select s
                ).First();

                lock (dest) {
                    dest.InUseCount++;
                    dest.IsValid = false;
                    dest.IsBeingGenerated = true;
                }
            }

            var m = Engine.ParticleMaterials.UpdatePositions;
            var e = m.Effect;
            using (var batch = NativeBatch.New(
                container, layer,
                m,
                (dm, _) => {
                    dm.PushRenderTargets(new[] {
                        new RenderTargetBinding(dest.PositionAndBirthTime),
                        new RenderTargetBinding(dest.Velocity)
                    });
                    dm.Device.Viewport = new Viewport(0, 0, Configuration.ParticlesPerRow, RowCount);
                    dm.Device.Clear(Color.Transparent);
                    e.Parameters["PositionTexture"].SetValue(source.PositionAndBirthTime);
                    e.Parameters["VelocityTexture"].SetValue(source.Velocity);
                    e.Parameters["HalfTexel"].SetValue(new Vector2(0.5f / Configuration.ParticlesPerRow, 0.5f / RowCount));
                    m.Flush();
                },
                (dm, _) => {
                    dm.PopRenderTarget();
                    e.Parameters["PositionTexture"].SetValue((Texture2D)null);
                    e.Parameters["VelocityTexture"].SetValue((Texture2D)null);
                    // fuck offfff
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, QuadVertexBuffer, 0,
                    QuadIndexBuffer, 0, 0, QuadVertexBuffer.VertexCount, 0, QuadVertexBuffer.VertexCount / 2
                ));
            }

            // TODO: Do this immediately after issuing the batch instead?
            Engine.Coordinator.AfterPresent(() => {
                lock (source)
                    source.InUseCount--;

                lock (dest) {
                    dest.InUseCount--;
                    dest.Timestamp = Util.Time.Ticks;
                    dest.IsValid = true;
                    dest.IsBeingGenerated = false;
                }
            });
        }

        public void Render (
            IBatchContainer container, int layer,
            Matrix? transform = null, 
            BlendState blendState = null
        ) {
            Slice source;

            lock (Slices) {
                source = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).First();

                lock (source)
                    source.InUseCount++;
            }

            var m = Engine.ParticleMaterials.RasterizeParticles;
            var e = m.Effect;
            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    e.Parameters["PositionTexture"].SetValue(source.PositionAndBirthTime);
                    e.Parameters["VelocityTexture"].SetValue(source.Velocity);
                    e.Parameters["HalfTexel"].SetValue(new Vector2(0.5f / Configuration.ParticlesPerRow, 0.5f / RowCount));
                    m.Flush();
                },
                (dm, _) => {
                    e.Parameters["PositionTexture"].SetValue((Texture2D)null);
                    e.Parameters["VelocityTexture"].SetValue((Texture2D)null);
                    // fuck offfff
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                int chunkCount = (RowCount + RasterChunkRowCount - 1) / RasterChunkRowCount;
                for (var i = 0; i < chunkCount; i++) {
                    var rowIndex = (i * RasterChunkRowCount);
                    var rowsToRender = Math.Min(RowCount - rowIndex, RasterChunkRowCount);
                    var quadCount = rowsToRender * Configuration.ParticlesPerRow;
                    var offset = new Vector2(0, rowIndex / (float)RowCount);
                    using (var chunk = NativeBatch.New(
                        group, i, m, (dm, _) => {
                            e.Parameters["SourceCoordinateOffset"].SetValue(offset);
                            m.Flush();
                        }
                    )) {
                        chunk.Add(new NativeDrawCall(
                            PrimitiveType.TriangleList, RasterizeVertexBuffer, 0,
                            RasterizeIndexBuffer, 0, 0, quadCount * 4, 0, quadCount * 2
                        ));
                    }
                }
            }

            // TODO: Do this immediately after issuing the batch instead?
            Engine.Coordinator.AfterPresent(() => {
                lock (source)
                    source.InUseCount--;
            });
        }

        public void Dispose () {
            foreach (var slice in Slices)
                Engine.Coordinator.DisposeResource(slice);

            Engine.Coordinator.DisposeResource(ParticleAgeFractionBuffer);
            Engine.Coordinator.DisposeResource(LiveParticleCountBuffer);
        }
    }

    public class ParticleSystemConfiguration {
        public readonly int MaximumCount;
        public readonly int ParticlesPerRow;

        // Particles that reach this age are killed
        // Defaults to (effectively) not killing particles
        public int MaximumAge = 1024 * 1024 * 8;

        public ParticleSystemConfiguration (
            int maximumCount = 4096,
            int particlesPerRow = 64
        ) {
            MaximumCount = maximumCount;
            ParticlesPerRow = particlesPerRow;
        }
    }
}
