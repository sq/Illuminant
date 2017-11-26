using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Squared.Illuminant.Uniforms {
    [StructLayout(LayoutKind.Sequential)]
    public struct Environment {
        public float   GroundZ;
        public float   ZToYMultiplier;
        public float   InvZToYMultiplier;
        public Vector2 RenderScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DistanceField {
        // FIXME: I should move these to another uniform set
        // StepLimit, MinimumLength, LongStepFactor
        public Vector3 Step;
        public float   MaxConeRadius;
        public float   ConeGrowthFactor;
        public float   ShadowDistanceFalloff;
        public float   OcclusionToOpacityPower;
        public float   InvScaleFactor;

        public Vector3 Extent;
        // X, Y, Total
        public Vector3 TextureSliceCount;
        public Vector2 TextureSliceSize;
        public Vector2 TextureTexelSize;

        // This does not initialize every member.
        public DistanceField (Squared.Illuminant.DistanceField df, float maximumZ) {
            Extent = new Vector3(
                df.VirtualWidth,
                df.VirtualHeight,
                maximumZ
            );
            TextureSliceSize = new Vector2(1f / df.ColumnCount, 1f / df.RowCount);
            TextureSliceCount = new Vector3(df.ColumnCount, df.RowCount, df.ValidSliceCount);
            TextureTexelSize = new Vector2(
                1f / (df.VirtualWidth * df.ColumnCount),
                1f / (df.VirtualHeight * df.RowCount)
            );
            InvScaleFactor = 1f / df.Resolution;

            Step = Vector3.Zero;
            MaxConeRadius = ConeGrowthFactor = ShadowDistanceFalloff =
                OcclusionToOpacityPower = 0;
        }
    }
}
