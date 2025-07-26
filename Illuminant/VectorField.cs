using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class VectorField : IDisposable {
        internal readonly Texture2D Texture;
        internal readonly RenderCoordinator Coordinator;

        public readonly bool OwnsTexture;
        public readonly bool HighPrecision;

        public VectorField (
            RenderCoordinator coordinator, int width, int height, bool highPrecision, bool ownsTexture = true
        ) {
            Coordinator = coordinator;
            HighPrecision = highPrecision;
            OwnsTexture = ownsTexture;

            Texture = new RenderTarget2D(
                coordinator.Device, width, height, false, 
                highPrecision ? SurfaceFormat.Vector4 : SurfaceFormat.Color, DepthFormat.None, 
                0, RenderTargetUsage.PreserveContents
            );
        }

        public VectorField (
            RenderCoordinator coordinator, Texture2D texture, bool ownsTexture = true
        ) {
            Coordinator = coordinator;
            HighPrecision = texture.Format == SurfaceFormat.Vector4;
            OwnsTexture = ownsTexture;
            Texture = texture;
        }

        public void Set<T> (T[] data)
            where T : struct
        {
            Texture.SetData(data);
        }

        public void Dispose () {
            if (OwnsTexture)
                Texture.Dispose();
        }
    }
}
