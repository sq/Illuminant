using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Illuminant.Configuration;
using Squared.Util;

namespace Squared.Illuminant {
    public interface IBezier {
        bool IsConstant { get; }
        int Count { get; set; }
        float MinValue { get; set; }
        float MaxValue { get; set; }
        object this [int index] { get; set; }
        object Evaluate (float t);
    }

    public interface IBezier<T> : IBezier {
        new T this [int index] { get; set; }
        new T Evaluate (float t);
        T A { get; set; }
        T B { get; set; }
        T C { get; set; }
        T D { get; set; }
        void SetConstant (T constant);
    }

    public class BezierF : IBezier<float> {
        public int Count { get; set; }
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public float A { get; set; }
        public float B { get; set; }
        public float C { get; set; }
        public float D { get; set; }

        public BezierF ()
            : this (1) {
        }

        public BezierF (float constant) {
            SetConstant(constant);
        }

        public void SetConstant (float constant) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = constant;
            B = C = D = 0;
        }

        public bool IsConstant {
            get {
                return Count == 1;
            }
        }

        public float this[int index] {
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
                this[index] = (float)value;
            }
        }

        public float Evaluate (float t) {
            if (Count <= 1)
                return A;

            var cb = new Uniforms.ClampedBezier1(this);
            return cb.Evaluate(t);
        }

        object IBezier.Evaluate (float t) {
            return Evaluate(t);
        }
    }

    public class Bezier2 : IBezier<Vector2> {
        public int Count { get; set; }
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public Vector2 A { get; set; }
        public Vector2 B { get; set; }
        public Vector2 C { get; set; }
        public Vector2 D { get; set; }

        public Bezier2 ()
            : this (Vector2.One) {
        }

        public Bezier2 (float x, float y)
            : this (new Vector2(x, y)) {
        }

        public Bezier2 (Vector2 constant) {
            SetConstant(constant);
        }

        public void SetConstant (Vector2 constant) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = constant;
            B = C = D = Vector2.Zero;
        }

        public bool IsConstant {
            get {
                return Count == 1;
            }
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

        public Vector2 Evaluate (float t) {
            if (Count <= 1)
                return A;

            var cb = new Uniforms.ClampedBezier2(this);
            return cb.Evaluate(t);
        }

        object IBezier.Evaluate (float t) {
            return Evaluate(t);
        }
    }

    public class Bezier4 : IBezier<Vector4> {
        public int Count { get; set; }
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public Vector4 A { get; set; }
        public Vector4 B { get; set; }
        public Vector4 C { get; set; }
        public Vector4 D { get; set; }

        public Bezier4 ()
            : this (Vector4.One) {
        }

        public Bezier4 (float x, float y, float z, float w)
            : this (new Vector4(x, y, z, w)) {
        }

        public Bezier4 (Vector4 constant) {
            SetConstant(constant);
        }

        public void SetConstant (Vector4 constant) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = constant;
            B = C = D = Vector4.Zero;
        }

        public bool IsConstant {
            get {
                return Count == 1;
            }
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

        public Vector4 Evaluate (float t) {
            if (Count <= 1)
                return A;

            var cb = new Uniforms.ClampedBezier4(this);
            return cb.Evaluate(t);
        }

        object IBezier.Evaluate (float t) {
            return Evaluate(t);
        }
    }

    public class BezierM : IBezier<DynamicMatrix> {
        public int Count { get; set; }
        public float MinValue { get; set; }
        public float MaxValue { get; set; }
        public DynamicMatrix A { get; set; }
        public DynamicMatrix B { get; set; }
        public DynamicMatrix C { get; set; }
        public DynamicMatrix D { get; set; }

        public BezierM ()
            : this (DynamicMatrix.Identity) {
        }

        public BezierM (Matrix constant) {
            SetConstant(new DynamicMatrix(constant));
        }

        public BezierM (DynamicMatrix constant) {
            SetConstant(constant);
        }

        public void SetConstant (DynamicMatrix constant) {
            Count = 1;
            MinValue = 0;
            MaxValue = 1;
            A = constant;
            B = C = D = DynamicMatrix.Identity;
        }

        public bool IsConstant {
            get {
                return Count == 1;
            }
        }

        public DynamicMatrix this[int index] {
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
                this[index] = (DynamicMatrix)value;
            }
        }

        public static void GetRowOfMatrix (int row, Matrix m, out Vector4 result) {
            GetRowOfMatrix(row, ref m, out result);
        }

        public static void GetRowOfMatrix (int row, ref Matrix m, out Vector4 result) {
            result = default(Vector4);
            switch (row) {
                case 0:
                    result = new Vector4(m.M11, m.M12, m.M13, m.M14);
                    return;
                case 1:
                    result = new Vector4(m.M21, m.M22, m.M23, m.M24);
                    return;
                case 2:
                    result = new Vector4(m.M31, m.M32, m.M33, m.M34);
                    return;
                case 3:
                    result = new Vector4(m.M41, m.M42, m.M43, m.M44);
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static void SetRowOfMatrix (int row, ref Matrix result, ref Vector4 value) {
            switch (row) {
                case 0:
                    result.M11 = value.X;
                    result.M12 = value.Y;
                    result.M13 = value.Z;
                    result.M14 = value.W;
                    return;
                case 1:
                    result.M21 = value.X;
                    result.M22 = value.Y;
                    result.M23 = value.Z;
                    result.M24 = value.W;
                    return;
                case 2:
                    result.M31 = value.X;
                    result.M32 = value.Y;
                    result.M33 = value.Z;
                    result.M34 = value.W;
                    return;
                case 3:
                    result.M41 = value.X;
                    result.M42 = value.Y;
                    result.M43 = value.Z;
                    result.M44 = value.W;
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public DynamicMatrix Evaluate (float t) {
            DynamicMatrix result = default(DynamicMatrix);
            DynamicMatrix A = this.A;
            A.Regenerate();

            if (Count <= 1)
                return A;

            DynamicMatrix B = this.B, C = this.C, D = this.D;
            var allDynamic = A.IsGenerated;
            if (Count > 1)
                allDynamic = allDynamic && B.IsGenerated;
            if (Count > 2)
                allDynamic = allDynamic && C.IsGenerated;
            if (Count > 3)
                allDynamic = allDynamic && D.IsGenerated;

            if (allDynamic) {
                Vector2 a = new Vector2(A.Angle, A.Scale), 
                    b = new Vector2(B.Angle, B.Scale),
                    c = new Vector2(C.Angle, C.Scale), 
                    d = new Vector2(D.Angle, D.Scale);
                var cb = new Uniforms.ClampedBezier2(this, 2, ref a, ref b, ref c, ref d);
                var p = cb.Evaluate(t);
                result.IsGenerated = true;
                result.Angle = p.X;
                result.Scale = p.Y;
                result.Regenerate();
            } else {
                B.Regenerate();
                C.Regenerate();
                D.Regenerate();
                Vector4 a, b, c, d;

                for (int i = 0; i < 4; i++) {
                    GetRowOfMatrix(i, ref A.Matrix, out a);
                    GetRowOfMatrix(i, ref B.Matrix, out b);
                    GetRowOfMatrix(i, ref C.Matrix, out c);
                    GetRowOfMatrix(i, ref D.Matrix, out d);
                    var cb4 = new Uniforms.ClampedBezier4(this, ref a, ref b, ref c, ref d);
                    var evaluated = cb4.Evaluate(t);
                    SetRowOfMatrix(i, ref result.Matrix, ref evaluated);
                }
            }

            return result;
        }

        object IBezier.Evaluate (float t) {
            return Evaluate(t);
        }
    }
}

namespace Squared.Illuminant.Uniforms {
    [StructLayout(LayoutKind.Sequential)]
    public struct ClampedBezier1 {
        public static readonly ClampedBezier1 One = new ClampedBezier1 {
            RangeAndCount = new Vector4(0, 1, 1, 1),
            ABCD = Vector4.One
        };

        public Vector4 RangeAndCount;
        public Vector4 ABCD;

        public ClampedBezier1 (BezierF src) : this() {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                1.0f / range, src.Count, 0
            );
            ABCD = new Vector4(
                src.A, src.B, src.C, src.D
            );
        }

        public float Evaluate (float value) {
            float a = ABCD.X, b = ABCD.Y, c = ABCD.Z, d = ABCD.W;

            float t;
            float count = ClampedBezier4.tForScaledBezier(RangeAndCount, value, out t);
            if (count <= 1.5)
                return a;

            float ab = Arithmetic.Lerp(a, b, t);
            if (count <= 2.5)
                return ab;

            float bc = Arithmetic.Lerp(b, c, t);
            float abbc = Arithmetic.Lerp(ab, bc, t);
            if (count <= 3.5)
                return abbc;

            float cd = Arithmetic.Lerp(c, d, t);
            float bccd = Arithmetic.Lerp(bc, cd, t);

            float result = Arithmetic.Lerp(abbc, bccd, t);
            return result;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ClampedBezier2 {
        public static readonly ClampedBezier2 One = new ClampedBezier2 {
            RangeAndCount = new Vector4(0, 1, 1, 2),
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
                1.0f / range, src.Count, 2
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

        public ClampedBezier2 (
            IBezier src, int elementCount, 
            ref Vector2 a, ref Vector2 b, ref Vector2 c, ref Vector2 d
        ) : this() {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                1.0f / range, src.Count, elementCount
            );
            AB = new Vector4(
                a.X, a.Y,
                b.X, b.Y
            );
            CD = new Vector4(
                c.X, c.Y,
                d.X, d.Y
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
            RangeAndCount = new Vector4(0, 1, 1, 4),
            A = Vector4.One,
            B = Vector4.One,
            C = Vector4.One,
            D = Vector4.One
        };

        public Vector4 RangeAndCount;
        public Vector4 A, B, C, D;

        public ClampedBezier4 (IBezier b) {
            var b1 = (b as BezierF);
            var b2 = (b as Bezier2);
            var b4 = (b as Bezier4);
            var bm = (b as BezierM);
            if (b1 != null)
                this = new ClampedBezier4(b1);
            else if (b2 != null)
                this = new ClampedBezier4(b2);
            else if (b4 != null)
                this = new ClampedBezier4(b4);
            else
                throw new ArgumentException();
        }

        public ClampedBezier4 (BezierF src, float y = 0, float z = 0, float w = 0) {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                1.0f / range, src.Count, 1
            );
            A = new Vector4(src.A, y, z, w);
            B = new Vector4(src.B, y, z, w);
            C = new Vector4(src.C, y, z, w);
            D = new Vector4(src.D, y, z, w);
        }

        public ClampedBezier4 (Bezier2 src, float z = 0, float w = 0) {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                1.0f / range, src.Count, 2
            );
            A = new Vector4(src.A, z, w);
            B = new Vector4(src.B, z, w);
            C = new Vector4(src.C, z, w);
            D = new Vector4(src.D, z, w);
        }

        public ClampedBezier4 (Bezier4 src) {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                1.0f / range, src.Count, 4
            );
            A = src.A;
            B = src.B;
            C = src.C;
            D = src.D;
        }

        public ClampedBezier4 (BezierM src, int row) {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;

            // HACK: Just visualize index?
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                1.0f / range, src.Count, 4
            );

            DynamicMatrix a = src.A, b = src.B, c = src.C, d = src.D;

            BezierM.GetRowOfMatrix(row, ref a.Matrix, out A);
            BezierM.GetRowOfMatrix(row, ref b.Matrix, out B);
            BezierM.GetRowOfMatrix(row, ref c.Matrix, out C);
            BezierM.GetRowOfMatrix(row, ref d.Matrix, out D);
        }

        public ClampedBezier4 (IBezier src, ref Vector4 a, ref Vector4 b, ref Vector4 c, ref Vector4 d) {
            if (src == null) {
                this = One;
                return;
            }

            var range = src.MaxValue - src.MinValue;
            if ((range == 0) || (src.Count <= 1))
                range = 1;
            RangeAndCount = new Vector4(
                Math.Min(src.MinValue, src.MaxValue),
                1.0f / range, src.Count, 4
            );
            A = a;
            B = b;
            C = c;
            D = d;
        }

        internal static int tForScaledBezier (Vector4 rangeAndCount, float value, out float t) {
            float minValue = rangeAndCount.X, 
                invDivisor = rangeAndCount.Y;

            t = (value - minValue) * Math.Abs(invDivisor);
            if (invDivisor < 0)
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