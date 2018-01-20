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
        // GroundZ, ZToYMultiplier, InvZToYMultiplier
        private Vector3 _Z;
        public Vector2 RenderScale;

        public float GroundZ {
            get {
                return _Z.X;
            }
            set {
                _Z.X = value;
            }
        }

        public float ZToYMultiplier {
            get {
                return _Z.Y;
            }
            set {
                _Z.Y = value;
            }
        }

        public float InvZToYMultiplier {
            get {
                return _Z.Z;
            }
            set {
                _Z.Z = value;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DistanceField {
        // MaxConeRadius, ConeGrowthFactor, OcclusionToOpacityPower, InvScaleFactor
        private Vector4 _ConeAndMisc;
        private Vector4 _TextureSliceAndTexelSize;
        // StepLimit, MinimumLength, LongStepFactor
        private Vector3 _Step;
        public Vector3 Extent;
        // X, Y, Total
        public Vector3 TextureSliceCount;

        // This does not initialize every member.
        public DistanceField (Squared.Illuminant.DistanceField df, float maximumZ) {
            Extent = df.GetExtent3(maximumZ);
            TextureSliceCount = new Vector3(df.ColumnCount, df.RowCount, df.ValidSliceCount);
            _TextureSliceAndTexelSize = new Vector4(
                1f / df.ColumnCount, 1f / df.RowCount,
                1f / (df.VirtualWidth * df.ColumnCount),
                1f / (df.VirtualHeight * df.RowCount)
            );

            _Step = new Vector3(0, 0, 1);
            _ConeAndMisc = new Vector4(0, 0, 0, 1f / df.Resolution);
        }

        public int StepLimit {
            get {
                return (int)_Step.X;
            }
            set {
                _Step.X = value;
            }
        }

        public float MinimumLength {
            get {
                return _Step.Y;
            }
            set {
                _Step.Y = value;
            }
        }

        public float LongStepFactor {
            get {
                return _Step.Z;
            }
            set {
                _Step.Z = value;
            }
        }

        public float MaxConeRadius {
            get {
                return _ConeAndMisc.X;
            }
            set {
                _ConeAndMisc.X = value;
            }
        }

        public float ConeGrowthFactor {
            get {
                return _ConeAndMisc.Y;
            }
            set {
                _ConeAndMisc.Y = value;
            }
        }

        public float OcclusionToOpacityPower {
            get {
                return _ConeAndMisc.Z;
            }
            set {
                _ConeAndMisc.Z = value;
            }
        }

        public float InvScaleFactor {
            get {
                return _ConeAndMisc.W;
            }
            set {
                _ConeAndMisc.W = value;
            }
        }
    }
}

namespace Squared.Illuminant {
    [StructLayout(LayoutKind.Sequential)]
    public struct Formula {
        public Vector4 Constant;
        public Vector4 RandomOffset;
        public Vector4 RandomScale;
        public float   RandomCircularity;
    }
}
