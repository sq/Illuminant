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
    public struct ClampedBezier2 {
        public static readonly ClampedBezier2 Zero = new ClampedBezier2 {
            Count = 1,
            MinValue = 0,
            InvDivisor = 1,
            A = Vector2.Zero,
            B = Vector2.Zero
        };

        public static readonly ClampedBezier2 One = new ClampedBezier2 {
            Count = 1,
            MinValue = 0,
            InvDivisor = 1,
            A = Vector2.One,
            B = Vector2.One
        };

        public Vector4 RangeAndCount;
        public Vector4 AB, CD;

        public ClampedBezier2 (Bezier2 src) : this() {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                src.MaxValue < src.MinValue
                    ? -1.0f / range
                    : 1.0f / range,
                src.Count, 0
            );
            AB = new Vector4(
                src.A.X, src.A.Y,
                src.B.X, src.B.Y
            );
            CD = new Vector4(
                src.C.X, src.C.Y,
                src.D.X, src.D.Y
            );
        }

        public Vector2 A {
            set {
                AB.X = value.X;
                AB.Y = value.Y;
            }
        }

        public Vector2 B {
            set {
                AB.Z = value.X;
                AB.W = value.Y;
            }
        }

        public float Count {
            set {
                RangeAndCount.Z = value;
            }
        }

        public float MinValue {
            set {
                RangeAndCount.X = value;
            }
        }

        public float InvDivisor {
            set {
                RangeAndCount.Y = value;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClampedBezier4 {
        public static readonly ClampedBezier4 Zero = new ClampedBezier4 {
            Count = 1,
            A = Vector4.Zero,
            B = Vector4.Zero,
            C = Vector4.Zero,
            D = Vector4.Zero
        };

        public static readonly ClampedBezier4 One = new ClampedBezier4 {
            Count = 1,
            A = Vector4.One,
            B = Vector4.One,
            C = Vector4.One,
            D = Vector4.One
        };

        public Vector4 RangeAndCount;
        public Vector4 A, B, C, D;

        public ClampedBezier4 (Bezier4 src) : this() {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                src.MaxValue < src.MinValue
                    ? -1.0f / range
                    : 1.0f / range,
                src.Count, 0
            );
            A = src.A;
            B = src.B;
            C = src.C;
            D = src.D;
        }

        public float Count {
            set {
                RangeAndCount.Z = value;
            }
        }

        public float MinValue {
            set {
                RangeAndCount.X = value;
            }
        }

        public float InvDivisor {
            set {
                RangeAndCount.Y = value;
            }
        }

        private int tForScaledBezier (float value, out float t) {
            float minValue = RangeAndCount.X, 
                invDivisor = RangeAndCount.Y;

            t = (value - minValue) * Math.Abs(invDivisor);
            if (invDivisor > 0)
                t = 1 - Arithmetic.Clamp(t, 0, 1);
            else
                t = Arithmetic.Clamp(t, 0, 1);
            return (int)RangeAndCount.Z;
        }

        /*
        public Vector2 Evaluate (float value) {
            Vector2 a = AB.xy,
                b = AB.zw,
                c = CD.xy,
                d = CD.zw;

            float t;
            float count = tForScaledBezier(bezier.RangeAndCount, value, t);
            return t;
            if (count <= 1.5)
                return a;

            float2 ab = lerp(a, b, t);
            if (count <= 2.5)
                return ab;

            float2 bc = lerp(b, c, t);
            float2 abbc = lerp(ab, bc, t);
            if (count <= 3.5)
                return abbc;

            float2 cd = lerp(c, d, t);
            float2 bccd = lerp(bc, cd, t);

            float2 result = lerp(abbc, bccd, t);
            return result;
        }
        */

        public Vector4 Evaluate (float value) {
            float t;
            int count = tForScaledBezier(value, out t);
            if (count <= 1.5)
                return A;

            var ab = Arithmetic.Lerp(A, B, t);
            if (count <= 2.5)
                return ab;

            var bc = Arithmetic.Lerp(B, C, t);
            var abbc = Arithmetic.Lerp(ab, bc, t);
            if (count <= 3.5)
                return abbc;

            var cd = Arithmetic.Lerp(C, D, t);
            var bccd = Arithmetic.Lerp(bc, cd, t);

            var result = Arithmetic.Lerp(abbc, bccd, t);
            return result;
        }
    }

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
        public Vector4 TexelAndSize;
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
            RotationFromLifeAndIndex = new Vector2(
                MathHelper.ToRadians(Configuration.RotationFromLife), 
                MathHelper.ToRadians(Configuration.RotationFromIndex)
            );
        }
    }
}

namespace Squared.Illuminant {
    [StructLayout(LayoutKind.Sequential)]
    public class Formula {
        public Vector4 Constant;
        public Vector4 RandomScale;
        public Vector4 Offset;
        public bool Circular;

        public Vector4 RandomOffset {
            set {
                if (Circular)
                    return;
                Offset = value;
            }
        }

        public Vector4 ConstantRadius {
            set {
                if (!Circular)
                    return;
                Offset = value;
            }
        }

        public static Formula UnitNormal () {
            var result = new Formula();
            result.SetToUnitNormal();
            return result;
        }

        public void SetToUnitNormal () {
            Constant = Vector4.Zero;
            Offset = Vector4.Zero;
            RandomScale = Vector4.One;
            Circular = true;
        }

        public void SetToConstant (Vector4 value) {
            Constant = value;
            Offset = RandomScale = Vector4.Zero;
            Circular = false;
        }

        public static Formula Zero () {
            return new Formula();
        }

        public static Formula One () {
            return new Formula {
                Constant = Vector4.One
            };
        }

        public Formula Clone () {
            return (Formula)MemberwiseClone();
        }
    }
}
