using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class ParticleSystem : IDisposable {
        private class Slice : IDisposable {
            public int  Index;
            public long Timestamp;
            public bool IsValid, IsBeingGenerated;
            public int  InUseCount;
            public RenderTarget2D PositionAndLife;
            public RenderTarget2D Velocity;

            public void Dispose () {
                IsValid = false;
                PositionAndLife.Dispose();
                Velocity.Dispose();
            }
        }

        public readonly int RowCount;
        public          int LiveCount { get; private set; }

        public readonly ParticleEngine              Engine;
        public readonly ParticleSystemConfiguration Configuration;

        private const int SliceCount      = 3;
        private const int ParticlesPerRow = 64;
        private readonly int[] DeadCountPerRow;

        private Slice[] Slices;
        private RenderTarget2D 
            // Used to locate empty slots to spawn new particles into
            // We generate this every frame from a pass over the position buffer
            ParticleAgeFractionBuffer,
            // We generate this every frame from a pass over the age fraction buffer
            LiveParticleCountBuffer;

        public ParticleSystem (
            ParticleEngine engine, ParticleSystemConfiguration configuration
        ) {
            Configuration = configuration;
            RowCount = (Configuration.MaximumCount + ParticlesPerRow - 1) / ParticlesPerRow;
            DeadCountPerRow = new int[RowCount];
            LiveCount = 0;

            lock (engine.Coordinator.CreateResourceLock) {
                Slices = AllocateSlices();

                // TODO: Bitpack?
                ParticleAgeFractionBuffer = new RenderTarget2D(
                    engine.Coordinator.Device,
                    ParticlesPerRow, RowCount, false,                    
                    SurfaceFormat.Alpha8, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );

                if (ParticlesPerRow >= 255)
                    throw new Exception("Live count per row is packed into 8 bits");
                LiveParticleCountBuffer = new RenderTarget2D(
                    engine.Coordinator.Device,
                    RowCount, 1, false,                    
                    SurfaceFormat.Alpha8, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );
            }
        }

        private Slice[] AllocateSlices () {
            var result = new Slice[SliceCount];
            for (var i = 0; i < result.Length; i++)
                result[i] = new Slice {
                    Index = i,
                    PositionAndLife = new RenderTarget2D(
                        Engine.Coordinator.Device,
                        ParticlesPerRow, RowCount, false,
                        SurfaceFormat.Vector4, DepthFormat.None,
                        0, RenderTargetUsage.PreserveContents
                    ),
                    Velocity = new RenderTarget2D(
                        Engine.Coordinator.Device,
                        ParticlesPerRow, RowCount, false,
                        SurfaceFormat.Vector4, DepthFormat.None,
                        0, RenderTargetUsage.PreserveContents
                    )
                };

            // HACK
            result[0].IsValid = true;

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

            // TODO: Actually update

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

        public void Render (IBatchContainer container, int layer) {
            Slice source;

            lock (Slices) {
                source = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).First();

                lock (source)
                    source.InUseCount++;
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

        // Particles that reach this age are killed
        // Defaults to (effectively) not killing particles
        public int MaximumAge = 1024 * 1024 * 8;

        public ParticleSystemConfiguration (int maximumCount = 8192) {
            MaximumCount = maximumCount;
        }
    }
}
