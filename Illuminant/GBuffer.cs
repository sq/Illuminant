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

        public readonly RenderTarget2D Texture;
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

            lock (coordinator.CreateResourceLock)
                Texture = new RenderTarget2D(
                    coordinator.Device, 
                    width, height, false, 
                    highQuality
                        ? SurfaceFormat.Vector4
                        : SurfaceFormat.HalfVector4,
                    DepthFormat.Depth24, 0, RenderTargetUsage.PlatformContents
                );

            Invalidate();
        }

        public void Invalidate () {
            IsValid = false;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            Texture.Dispose();
        }
    }
}
