using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class GBuffer : IDisposable {
        public bool IsDisposed { get; private set; }
        public bool IsValid    { get; internal set; }

        public readonly RenderCoordinator Coordinator;
        public readonly AutoRenderTarget Texture;
        public readonly Vector2 Size, InverseSize;
        public readonly int Width, Height;

        public GBuffer (
            RenderCoordinator coordinator, 
            int width, int height, 
            bool highQuality = true
        ) {
            Width = width;
            Height = height;
            Size = new Vector2(Width, Height);
            InverseSize = new Vector2(1.0f / Width, 1.0f / Height);
            Coordinator = coordinator;

            lock (coordinator.CreateResourceLock)
                Texture = new AutoRenderTarget(
                    coordinator, 
                    width, height, false, 
                    highQuality
                        ? SurfaceFormat.Vector4
                        : SurfaceFormat.HalfVector4,
                    DepthFormat.Depth24, 0
                );

            coordinator.DeviceReset += Coordinator_DeviceReset;

            Invalidate();
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            Invalidate();
        }

        public void Invalidate () {
            IsValid = false;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            // TODO: Remove event from coordinator

            IsDisposed = true;
            Coordinator.DisposeResource(Texture);
        }
    }
}
