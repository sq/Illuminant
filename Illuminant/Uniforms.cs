using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Util;

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
        // MaxConeRadius, ConeGrowthFactor, OcclusionToOpacityPower, InvScaleFactorX
        private Vector4 _ConeAndMisc;
        private Vector4 _TextureSliceAndTexelSize;
        // StepLimit, MinimumLength, LongStepFactor, InvScaleFactorY
        private Vector4 _StepAndMisc2;
        // X, Y, MaximumZ, SliceCount
        public Vector4 TextureSliceCount;
        public Vector4 Extent;

        // This does not initialize every member.
        public DistanceField (Squared.Illuminant.DistanceField df, float maximumZ) {
            Extent = df.GetExtent4(maximumZ);
            // FIXME
            float sliceZSize = maximumZ / df.SliceCount;
            TextureSliceCount = new Vector4(
                df.ColumnCount, df.RowCount, 
                Math.Min(df.SliceInfo.ValidSliceCount, df.SliceCount) * sliceZSize,
                df.SliceCount
            );
            _TextureSliceAndTexelSize = new Vector4(
                1f / df.ColumnCount, 1f / df.RowCount,
                1f / (df.VirtualWidth * df.ColumnCount),
                1f / (df.VirtualHeight * df.RowCount)
            );

            _ConeAndMisc = new Vector4(0, 0, 0, (float)((double)df.VirtualWidth / df.SliceWidth));
            _StepAndMisc2 = new Vector4(0, 0, 1, (float)((double)df.VirtualHeight / df.SliceHeight));
        }

        public int StepLimit {
            get {
                return (int)_StepAndMisc2.X;
            }
            set {
                _StepAndMisc2.X = value;
            }
        }

        public float MinimumLength {
            get {
                return _StepAndMisc2.Y;
            }
            set {
                _StepAndMisc2.Y = value;
            }
        }

        public float LongStepFactor {
            get {
                return _StepAndMisc2.Z;
            }
            set {
                _StepAndMisc2.Z = value;
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

        public float InvScaleFactorX {
            get {
                return _ConeAndMisc.W;
            }
            set {
                _ConeAndMisc.W = value;
            }
        }

        public float InvScaleFactorY {
            get {
                return _StepAndMisc2.W;
            }
            set {
                _StepAndMisc2.W = value;
            }
        }
    }

    public struct ParticleSystem {
        // deltaTimeMilliseconds, friction, maximumVelocity, lifeDecayRate
        public Vector4 GlobalSettings;
        // escapeVelocity, bounceVelocityMultiplier, collisionDistance, collisionLifePenalty
        public Vector4 CollisionSettings;
        public Vector4 ColorFromLife;
        public Vector4 TexelAndSize;
        public Vector2 SizeFromLife;
        public Vector2 RotationFromLifeAndIndex;

        public ParticleSystem (
            Particles.ParticleEngineConfiguration Engine,
            Particles.ParticleSystemConfiguration Configuration,
            double deltaTimeSeconds
        ) {
            TexelAndSize = new Vector4(
                1f / Engine.ChunkSize, 1f / Engine.ChunkSize,
                Configuration.Size.X, Configuration.Size.Y
            );
            GlobalSettings = new Vector4(
                (float)(deltaTimeSeconds * 1000), Configuration.Friction, 
                Configuration.MaximumVelocity, Configuration.GlobalLifeDecayRate
            );
            CollisionSettings = new Vector4(
                Configuration.EscapeVelocity,
                Configuration.BounceVelocityMultiplier,
                Configuration.CollisionDistance,
                Configuration.CollisionLifePenalty
            );
            ColorFromLife = Configuration._ColorFromLife.GetValueOrDefault(Vector4.Zero);
            SizeFromLife = Configuration.SizeFromLife.GetValueOrDefault(Vector2.Zero);
            RotationFromLifeAndIndex = new Vector2(
                MathHelper.ToRadians(Configuration.RotationFromLife), 
                MathHelper.ToRadians(Configuration.RotationFromIndex)
            );
        }
    }
}

namespace Squared.Illuminant {
    [StructLayout(LayoutKind.Sequential)]
    public struct Formula {
        public Vector4 Constant;
        public Vector4 RandomOffset;
        public Vector4 RandomScale;
        public Vector4 RandomScaleConstant;
        public bool Circular;
    }
}
