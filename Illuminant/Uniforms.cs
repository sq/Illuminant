using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Illuminant.Particles;
using Squared.Util;

namespace Squared.Illuminant.Uniforms {
    [StructLayout(LayoutKind.Sequential)]
    public struct Environment {
        // GroundZ, ZToYMultiplier, InvZToYMultiplier
        private Vector4 ZAndScale;

        public float GroundZ {
            get {
                return ZAndScale.X;
            }
            set {
                ZAndScale.X = value;
            }
        }

        public float ZToYMultiplier {
            get {
                return ZAndScale.Y;
            }
            set {
                ZAndScale.Y = value;
            }
        }

        public Vector2 RenderScale {
            get {
                return new Vector2(ZAndScale.Z, ZAndScale.W);
            }
            set {
                ZAndScale.Z = value.X;
                ZAndScale.W = value.Y;
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

    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleSystem {
        public const int VelocityConstantScale = 1000;

        // deltaTimeMilliseconds, friction, maximumVelocity, lifeDecayRate
        public Vector4 GlobalSettings;
        // escapeVelocity, bounceVelocityMultiplier, collisionDistance, collisionLifePenalty
        public Vector4 CollisionSettings;
        public Vector4 TexelAndSize;
        public Vector4 AnimationRateAndRotationAndZToY;
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
                (float)(deltaTimeSeconds * VelocityConstantScale), Configuration.Friction, 
                Configuration.MaximumVelocity, Configuration.LifeDecayPerSecond
            );
            if (Configuration.Collision != null)
                CollisionSettings = new Vector4(
                    Configuration.Collision.EscapeVelocity,
                    Configuration.Collision.BounceVelocityMultiplier,
                    Configuration.Collision.Distance,
                    Configuration.Collision.LifePenalty
                );
            else
                CollisionSettings = Vector4.Zero;
            RotationFromLifeAndIndex = new Vector2(
                MathHelper.ToRadians(Configuration.RotationFromLife), 
                MathHelper.ToRadians(Configuration.RotationFromIndex)
            );
            var ar = Configuration.Appearance != null ? Configuration.Appearance.AnimationRate : Vector2.Zero;
            AnimationRateAndRotationAndZToY = new Vector4(
                (ar.X != 0) ? 1.0f / ar.X : 0, (ar.Y != 0) ? 1.0f / ar.Y : 0,
                Configuration.RotationFromVelocity ? 1f : 0f, Configuration.ZToY
            );            
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RasterizeParticleSystem {
        Vector4 GlobalColor;
        Vector4 BitmapTextureRegion;
        Vector2 SizeFactor;

        public RasterizeParticleSystem (
            Particles.ParticleEngineConfiguration Engine,
            Particles.ParticleSystemConfiguration Configuration
        ) {
            var appearance = Configuration.Appearance;

            var tex = appearance?.Texture?.Instance;
            if ((tex != null) && tex.IsDisposed)
                tex = null;
            var texSize = (tex != null)
                ? new Vector2(tex.Width, tex.Height)
                : Vector2.One;

            // TODO: transform arg
            if (tex != null) {
                // var offset = new Vector2(-0.5f) / texSize;
                var offset = appearance.OffsetPx / texSize;
                var size = appearance.SizePx.GetValueOrDefault(texSize) / texSize;
                BitmapTextureRegion = new Vector4(
                    offset.X, offset.Y, 
                    offset.X + size.X, offset.Y + size.Y
                );
            } else {
                BitmapTextureRegion = new Vector4(0, 0, 1, 1);
            }
            if ((tex != null) && appearance.RelativeSize)
                SizeFactor = appearance.SizePx.GetValueOrDefault(texSize) * 0.5f;
            else
                SizeFactor = Vector2.One;

            var gcolor = Configuration.Color.Global;
            gcolor.X *= gcolor.W;
            gcolor.Y *= gcolor.W;
            gcolor.Z *= gcolor.W;
            GlobalColor = gcolor;
        }
    }
}