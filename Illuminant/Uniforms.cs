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
        public Vector2 RenderScale;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DistanceField {
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
    }
}
