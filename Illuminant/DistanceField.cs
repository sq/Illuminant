using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class DistanceField : IDisposable {
        public bool IsDisposed { get; private set; }

        public readonly float VirtualWidth, VirtualHeight, VirtualDepth;
        public readonly float Resolution;

        public readonly RenderTarget2D Texture;
        public readonly int SliceWidth, SliceHeight, SliceCount;
        public readonly int PhysicalSliceCount;
        public readonly int ColumnCount, RowCount;

        public int ValidSliceCount { get; internal set; }

        internal readonly List<int> InvalidSlices = new List<int>();

        public DistanceField (
            RenderCoordinator coordinator,
            float virtualWidth, float virtualHeight, float virtualDepth,
            int sliceCount, float resolution = 1f
        ) {
            VirtualWidth = virtualWidth;
            VirtualHeight = virtualHeight;
            VirtualDepth = virtualDepth;
            Resolution = resolution;

            SliceWidth = (int)Math.Ceiling(virtualWidth * resolution);
            SliceHeight = (int)Math.Ceiling(virtualHeight * resolution);
            int maxSlicesX = 4096 / SliceWidth;
            int maxSlicesY = 4096 / SliceHeight;
            int maxSlices = maxSlicesX * maxSlicesY * LightingRenderer.PackedSliceCount;
                
            // HACK: If they ask for too many slices we give them as many as we can.
            SliceCount = Math.Min(sliceCount, maxSlices);

            PhysicalSliceCount = (int)Math.Ceiling(SliceCount / (float)LightingRenderer.PackedSliceCount);

            ColumnCount = Math.Min(maxSlicesX, PhysicalSliceCount);
            RowCount = Math.Max((int)Math.Ceiling(PhysicalSliceCount / (float)maxSlicesX), 1);

            lock (coordinator.CreateResourceLock)
                Texture = new RenderTarget2D(
                    coordinator.Device,
                    SliceWidth * ColumnCount, 
                    SliceHeight * RowCount,
                    false, SurfaceFormat.Rgba64,
                    DepthFormat.None, 0, 
                    RenderTargetUsage.PlatformContents
                );

            Invalidate();
        }

        public void Invalidate () {
            for (var i = 0; i < SliceCount; i++) {
                if (InvalidSlices.Contains(i))
                    continue;

                InvalidSlices.Add(i);
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Texture.Dispose();
        }
    }
}
