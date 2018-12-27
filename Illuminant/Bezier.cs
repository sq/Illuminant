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
    public interface IBezier {
        int Count { get; set; }
        float MinValue { get; set; }
        float MaxValue { get; set; }
        object this [int index] { get; set; }
    }

    public class Bezier2 : IBezier {
        public int Count { get; set; }
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public Vector2 A, B, C, D;

        public Bezier2 ()
            : this (Vector2.One) {
        }

        public Bezier2 (float x, float y)
            : this (new Vector2(x, y)) {
        }

        public Bezier2 (Vector2 constant) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = B = C = D = constant;
        }

        public Vector2 this[int index] {
            get {
                switch (index) {
                    case 0:
                        return A;
                    case 1:
                        return B;
                    case 2:
                        return C;
                    case 3:
                        return D;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
            set {
                switch (index) {
                    case 0:
                        A = value;
                        return;
                    case 1:
                        B = value;
                        return;
                    case 2:
                        C = value;
                        return;
                    case 3:
                        D = value;
                        return;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }

        object IBezier.this[int index] {
            get {
                return this[index];
            }
            set {
                this[index] = (Vector2)value;
            }
        }
    }

    public class Bezier4 : IBezier {
        public int Count { get; set; }
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public Vector4 A, B, C, D;

        public Bezier4 ()
            : this (Vector4.One) {
        }

        public Bezier4 (float x, float y, float z, float w)
            : this (new Vector4(x, y, z, w)) {
        }

        public Bezier4 (Vector4 constant) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = B = C = D = constant;
        }

        public Vector4 this[int index] {
            get {
                switch (index) {
                    case 0:
                        return A;
                    case 1:
                        return B;
                    case 2:
                        return C;
                    case 3:
                        return D;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
            set {
                switch (index) {
                    case 0:
                        A = value;
                        return;
                    case 1:
                        B = value;
                        return;
                    case 2:
                        C = value;
                        return;
                    case 3:
                        D = value;
                        return;
                    default:
                        throw new IndexOutOfRangeException();
                }
            }
        }

        object IBezier.this[int index] {
            get {
                return this[index];
            }
            set {
                this[index] = (Vector4)value;
            }
        }
    }
}

namespace Squared.Illuminant.Uniforms {
    [StructLayout(LayoutKind.Sequential)]
    public struct ClampedBezier2 {
        public static readonly ClampedBezier2 One = new ClampedBezier2 {
            RangeAndCount = new Vector4(0, 1, 1, 0),
            AB = Vector4.One,
            CD = Vector4.One
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
        public static readonly ClampedBezier4 One = new ClampedBezier4 {
            RangeAndCount = new Vector4(0, 1, 1, 0),
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