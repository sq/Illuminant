using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Util;

namespace Squared.Illuminant {
    [TypeConverter(typeof(BezierConverter))]
    public class Bezier2 {
        public int Count;
        public float MinValue, MaxValue;
        public Vector2 A, B, C, D;

        public Bezier2 (float x, float y) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = B = C = D = new Vector2(x, y);
        }
    }

    [TypeConverter(typeof(BezierConverter))]
    public class Bezier4 {
        public int Count;
        public float MinValue, MaxValue;
        public Vector4 A, B, C, D;

        public Bezier4 (float x, float y, float z, float w) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = B = C = D = new Vector4(x, y, z, w);
        }
    }

    public class BezierConverter : TypeConverter {
        public override bool CanConvertFrom (ITypeDescriptorContext context, Type sourceType) {
            if (sourceType == typeof(string))
                return true;

            return base.CanConvertFrom(context, sourceType);
        }

        public override object ConvertFrom (ITypeDescriptorContext context, CultureInfo culture, object value) {
            var s = value as string;
            if (s != null) {
                var values = (from p in s.Split(',') select float.Parse(p.Trim())).ToList();
                if (values.Count == 4)
                    return new Bezier4(values[0], values[1], values[2], values[3]);
                else if (values.Count == 2)
                    return new Bezier2(values[0], values[1]);
            }

            return base.ConvertFrom(context, culture, value);
        }

        public override bool CanConvertTo (ITypeDescriptorContext context, Type destinationType) {
            if (
                (destinationType == typeof(Bezier2)) ||
                (destinationType == typeof(Bezier4))
            )
                return true;

            return base.CanConvertTo(context, destinationType);
        }

        public override object ConvertTo (ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) {
            throw new Exception("NYI");

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}

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

        public Vector2 Evaluate (float value) {
            Vector2 a = new Vector2(AB.X, AB.Y),
                b = new Vector2(AB.Z, AB.W),
                c = new Vector2(CD.X, CD.Y),
                d = new Vector2(CD.Z, CD.W);

            float t;
            float count = ClampedBezier4.tForScaledBezier(RangeAndCount, value, out t);
            if (count <= 1.5)
                return a;

            Vector2 ab = Arithmetic.Lerp(a, b, t);
            if (count <= 2.5)
                return ab;

            Vector2 bc = Arithmetic.Lerp(b, c, t);
            Vector2 abbc = Arithmetic.Lerp(ab, bc, t);
            if (count <= 3.5)
                return abbc;

            Vector2 cd = Arithmetic.Lerp(c, d, t);
            Vector2 bccd = Arithmetic.Lerp(bc, cd, t);

            Vector2 result = Arithmetic.Lerp(abbc, bccd, t);
            return result;
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

        internal static int tForScaledBezier (Vector4 rangeAndCount, float value, out float t) {
            float minValue = rangeAndCount.X, 
                invDivisor = rangeAndCount.Y;

            t = (value - minValue) * Math.Abs(invDivisor);
            if (invDivisor > 0)
                t = 1 - Arithmetic.Clamp(t, 0, 1);
            else
                t = Arithmetic.Clamp(t, 0, 1);
            return (int)rangeAndCount.Z;
        }

        public Vector4 Evaluate (float value) {
            float t;
            int count = tForScaledBezier(RangeAndCount, value, out t);
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
}