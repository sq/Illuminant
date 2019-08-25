using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Util;

namespace Squared.Illuminant.Configuration {
    public enum Operators : int {
        Identity = 0,
        Add = 1,
        Subtract = 2,
        Multiply = 3,

        Normalize = 10
    }

    public interface IParameterExpression {
        IParameter LeftHandSide { get; set; }
        IParameter RightHandSide { get; set; }
        Operators Operator { get; set; }
        Type ValueType { get; }
    }

    public interface IParameter {
        Type ValueType { get; }
        bool IsConstant { get; }
        bool IsBezier { get; }
        bool IsReference { get; }
        bool IsExpression { get; }
        IParameter ToBezier ();
        IParameter ToConstant ();
        IParameter ToReference ();
        IParameter ToExpression ();
        string Name { get; set; }
        object Constant { get; set; }
        IBezier Bezier { get; set; }
        IParameterExpression Expression { get; set; }
        object EvaluateBoxed (float t, Delegate nameResolver);
    }

    internal interface IInternalParameter : IParameter {
        void Set (string name, object bezier, object constant);
    }

    public delegate bool NamedConstantResolver<T> (string name, float t, out T result);

    internal class SpecialOperatorImplementations {
        public static Vector2 Normalize (Vector2 lhs, Vector2 rhs) {
            lhs.Normalize();
            return lhs;
        }

        public static Vector3 Normalize (Vector3 lhs, Vector3 rhs) {
            lhs.Normalize();
            return lhs;
        }

        public static Vector4 Normalize (Vector4 lhs, Vector4 rhs) {
            lhs.Normalize();
            return lhs;
        }
    }

    public class BinaryParameterExpression<T> : IParameterExpression
        where T : struct
    {
        public static readonly Dictionary<Operators, int> OperandCounts = new Dictionary<Operators, int> {
            {Operators.Identity, 1 },
            {Operators.Add, 2 },
            {Operators.Subtract, 3 },
            {Operators.Multiply, 4 },
            {Operators.Normalize, 10 },
        };
        private static readonly Dictionary<Operators, Func<T, T, T>> SpecialOperatorCache = new Dictionary<Operators, Func<T, T, T>>();

        public Operators Operator;
        public Parameter<T> LeftHandSide, RightHandSide;

        [NonSerialized]
        private bool _IsEvaluating;

        static BinaryParameterExpression () {
            var tImpl = typeof(SpecialOperatorImplementations);
            var tDel = typeof(Func<T, T, T>);

            foreach (var ov in Enum.GetValues(typeof(Operators))) {
                var ev = (Operators)ov;
                int iv = (int)ov;
                if (iv < (int)Operators.Normalize)
                    continue;

                var methodName = ev.ToString();
                var parameterTypes = new [] { typeof(T), typeof(T) };
                var method = tImpl.GetMethod(
                    methodName, 
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                    null, parameterTypes, null
                );

                if (method != null)
                    SpecialOperatorCache[ev] = (Func<T, T, T>)Delegate.CreateDelegate(tDel, method);
            }
        }

        public BinaryParameterExpression (Parameter<T> lhs, Operators op, Parameter<T> rhs) {
            Operator = op;
            LeftHandSide = lhs;
            RightHandSide = rhs;
        }

        public T Evaluate (float t, NamedConstantResolver<T> nameResolver) {
            if (_IsEvaluating) {
                // throw new StackOverflowException("Recursion in parameter");
                return default(T);
            }

            _IsEvaluating = true;
            try {
                var lhs = LeftHandSide.Evaluate(t, nameResolver);
                if (Operator == 0)
                    return lhs;

                var rhs = RightHandSide.Evaluate(t, nameResolver);
                T result;
                if (Operator == Operators.Identity)
                    result = lhs;
                else if (TryInvokeSpecialOperator(Operator, ref lhs, ref rhs, out result))
                    ;
                else
                    result = Arithmetic.InvokeOperator((Arithmetic.Operators)(int)Operator, lhs, rhs);
                return result;
            } finally {
                _IsEvaluating = false;
            }
        }

        public static bool TryInvokeSpecialOperator (Operators op, ref T lhs, ref T rhs, out T result) {
            result = default(T);
            if (op < Operators.Normalize)
                return false;

            Func<T, T, T> fn;
            if (SpecialOperatorCache.TryGetValue(op, out fn)) {
                result = fn(lhs, rhs);
                return true;
            }

            return false;
        }

        public Type ValueType {
            get {
                return LeftHandSide.ValueType ?? RightHandSide.ValueType;
            }
        }

        IParameter IParameterExpression.LeftHandSide {
            get {
                return LeftHandSide;
            }
            set {
                LeftHandSide = (Parameter<T>)value;
            }
        }

        IParameter IParameterExpression.RightHandSide {
            get {
                return RightHandSide;
            }
            set {
                RightHandSide = (Parameter<T>)value;
            }
        }

        Operators IParameterExpression.Operator {
            get {
                return Operator;
            }
            set {
                Operator = value;
            }
        }
    }

    [TypeConverter(typeof(ParameterConverter))]
    public struct Parameter<T> : IInternalParameter
        where T : struct
    {
        public const string Unnamed = "<<none>>";

        private Func<float, T> _Getter;
        private string _Name;
        private IBezier<T> _Bezier;
        private T _Constant;
        private BinaryParameterExpression<T> _Expression;

        public Parameter (IBezier<T> bezier) {
            _Name = null;
            _Bezier = bezier;
            _Constant = default(T);
            _Getter = null;
            _Expression = null;
        }

        public Parameter (T value) {
            _Name = null;
            _Bezier = null;
            _Constant = value;
            _Getter = null;
            _Expression = null;
        }

        public Parameter (string name) {
            _Name = name;
            _Getter = null;
            _Bezier = null;
            _Constant = default(T);
            _Expression = null;
        }

        public Parameter (string name, Func<float, T> getter) {
            _Name = name;
            _Getter = getter;
            _Bezier = null;
            _Constant = default(T);
            _Expression = null;
        }

        public Parameter (BinaryParameterExpression<T> expression) {
            _Name = null;
            _Getter = null;
            _Bezier = null;
            _Constant = default(T);
            _Expression = expression;
        }

        public string Name {
            get {
                return _Name;
            }
            set {
                _Name = value;
            }
        }

        public IBezier<T> Bezier {
            get {
                if (_Bezier != null)
                    return _Bezier;
                else
                    return null;
            }
            set {
                _Bezier = value;
            }
        }

        public T Constant {
            get {
                if (_Bezier != null) {
                    if (_Bezier.IsConstant)
                        return _Bezier.A;
                    else
                        return default(T);
                } else
                    return _Constant;
            }
            set {
                if (_Bezier != null)
                    _Bezier.SetConstant(value);
                else
                    _Constant = value;
            }
        }

        public BinaryParameterExpression<T> Expression {
            get {
                return _Expression;
            }
            set {
                _Expression = value;
                if (value != null) {
                    _Constant = default(T);
                    _Bezier = null;
                    _Name = null;
                    _Getter = null;
                }
            }
        }

        public Type ValueType {
            get {
                return typeof(T);
            }
        }

        public bool IsConstant {
            get {
                return ((_Name == null) && (_Bezier == null) && (_Expression == null)) || 
                    (_Bezier != null) && _Bezier.IsConstant;
            }
        }

        public bool IsBezier {
            get {
                return (_Name == null) && (_Bezier != null) && (_Expression == null);
            }
        }

        public bool IsReference {
            get {
                return (_Name != null) && (_Expression == null);
            }
        }

        public bool IsExpression {
            get {
                return _Expression != null;
            }
        }

        private bool TryConvertToBezier () {
            if (IsBezier)
                return true;

            if (!IsConstant)
                return false;

            switch (ValueType.Name) {
                case "Single":
                    _Bezier = (IBezier<T>)new BezierF(Convert.ToSingle(Constant));
                    return true;
                case "Vector2":
                    _Bezier = (IBezier<T>)new Bezier2((Vector2)Convert.ChangeType(Constant, ValueType));
                    return true;
                case "Vector3":
                    _Bezier = (IBezier<T>)new Bezier3((Vector3)Convert.ChangeType(Constant, ValueType));
                    return true;
                case "Vector4":
                    _Bezier = (IBezier<T>)new Bezier4((Vector4)Convert.ChangeType(Constant, ValueType));
                    return true;
                case "Matrix":
                    _Bezier = (IBezier<T>)new BezierM((Matrix)Convert.ChangeType(Constant, ValueType));
                    return true;
                case "DynamicMatrix":
                    _Bezier = (IBezier<T>)new BezierM((DynamicMatrix)Convert.ChangeType(Constant, ValueType));
                    return true;
                default:
                    return false;
            }
        }

        public Parameter<T> ToReference () {
            if (IsReference)
                return this;

            var result = Clone();
            result._Name = Unnamed;
            return result;
        }

        public Parameter<T> ToConstant () {
            var result = this;
            if (IsReference)
                // TODO: Copy constant/evaluated value from reference?
                result._Name = null;
            else if (IsBezier)
                result.Constant = _Bezier.A;
            result.Bezier = null;

            return result;
        }

        public Parameter<T> ToExpression () {
            var expr = new BinaryParameterExpression<T>(
                this, Operators.Identity, new Parameter<T>(default(T))
            );
            return new Parameter<T>(expr);
        }

        public Parameter<T> ToBezier () {
            var result = this;
            if (IsReference)
                result._Name = null;

            if (!IsBezier)
                if (!result.TryConvertToBezier())
                    throw new Exception();
            return result;
        }

        IParameter IParameter.ToExpression () {
            return ToExpression();
        }

        IParameter IParameter.ToReference () {
            return ToReference();
        }

        IParameter IParameter.ToConstant () {
            return ToConstant();
        }

        IParameter IParameter.ToBezier () {
            return ToBezier();
        }

        IParameterExpression IParameter.Expression {
            get {
                return _Expression;
            }
            set {
                _Expression = (BinaryParameterExpression<T>)value;
            }
        }

        IBezier IParameter.Bezier {
            get {
                return _Bezier;
            }
            set {
                Bezier = (IBezier<T>)value;
            }
        }

        object IParameter.Constant {
            get {
                return Constant;
            }
            set {
                Constant = (T)value;
            }
        }

        public T Evaluate (float t, NamedConstantResolver<T> nameResolver) {
            if (_Expression != null)
                return _Expression.Evaluate(t, nameResolver);

            if (_Getter != null)
                return _Getter(t);

            T resolved;
            if (
                (_Name != null) &&
                (nameResolver != null) &&
                nameResolver(_Name, t, out resolved)
            )
                return resolved;

            if (_Bezier != null)
                return _Bezier.Evaluate(t);
            else
                return _Constant;
        }

        public object EvaluateBoxed (float t, Delegate nameResolver) {
            return Evaluate(t, (NamedConstantResolver<T>)nameResolver);
        }

        public static implicit operator Parameter<T> (T value) {
            return new Parameter<T>(value);
        }

        public Parameter<T> Clone () {
            return new Parameter<T> {
                _Name = _Name,
                _Bezier = _Bezier,
                _Constant = _Constant,
                _Getter = _Getter,
                _Expression = _Expression
            };
        }

        void IInternalParameter.Set (string name, object bezier, object constant) {
            _Name = name;
            _Bezier = (IBezier<T>)bezier;
            if (constant != null)
                _Constant = (T)Convert.ChangeType(constant, typeof(T));
            else
                _Constant = default(T);
        }
    }

    public struct DynamicMatrix {
        public static readonly DynamicMatrix Identity = new DynamicMatrix(Matrix.Identity);

        public bool    IsGenerated;
        public float   AngleX, AngleY, AngleZ;
        public float   Scale;
        public Vector3 Translation;
        public Matrix  Matrix;

        public float Angle {
            get {
                return AngleZ;
            }
            set {
                AngleZ = value;
            }
        }

        public DynamicMatrix (float angleX, float angleY, float angleZ, float scale, Vector3 translation) {
            Matrix = default(Matrix);
            Scale = scale;
            AngleX = angleX;
            AngleY = angleY;
            AngleZ = angleZ;
            Translation = translation;
            IsGenerated = true;
        }

        public DynamicMatrix (float angleZ, float scale, Vector3 translation) {
            Matrix = default(Matrix);
            Scale = scale;
            AngleX = AngleY = 0;
            AngleZ = angleZ;
            Translation = translation;
            IsGenerated = true;
        }

        public DynamicMatrix (Matrix matrix) {
            Matrix = matrix;
            Scale = 1;
            AngleX = AngleY = 0;
            AngleZ = 0;
            Translation = Vector3.Zero;
            IsGenerated = false;
        }

        public void Regenerate () {
            if (!IsGenerated)
                return;

            var rotation = new Vector3(
                MathHelper.ToRadians(AngleX),
                MathHelper.ToRadians(AngleY),
                MathHelper.ToRadians(AngleZ)
            );

            Matrix rot;

            if (rotation != Vector3.Zero) {
                Quaternion quat;
                Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z, out quat);
                quat.Normalize();
                rot = Matrix.CreateFromQuaternion(quat);
            } else {
                rot = Matrix.Identity;
            }

            Matrix.Multiply(ref rot, Scale, out Matrix);
            Matrix *= Matrix.CreateTranslation(Translation);
        }
    }

    public struct ParticleSystemReference {
        private int?   _Index;
        private string _Name;

        [NonSerialized]
        public Particles.ParticleSystem Instance;

        public int? Index {
            get {
                return _Index;
            }
            set {
                if (value == _Index)
                    return;

                Instance = null;
                _Index = value;
            }
        }

        public string Name {
            get {
                return _Name;
            }
            set {
                if (value == _Name)
                    return;

                Instance = null;
                _Name = value;
            }
        }

        public bool TryInitialize (Func<string, int?, Particles.ParticleSystem> resolver) {
            if ((Instance != null) && Instance.IsDisposed)
                Instance = null;

            if (Instance != null)
                return true;

            if (resolver != null)
                Instance = resolver(Name, Index);

            return Instance != null;
        }

        public static implicit operator ParticleSystemReference (Particles.ParticleSystem instance) {
            return new ParticleSystemReference {
                Instance = instance
            };
        }
    }

    public class ParameterConverter : TypeConverter {
        public override bool CanConvertTo (ITypeDescriptorContext context, Type destinationType) {
            if (typeof(IParameter).IsAssignableFrom(destinationType))
                return true;

            return false;
        }

        public override bool CanConvertFrom (ITypeDescriptorContext context, Type sourceType) {
            if (
                (sourceType == typeof(int)) ||
                (sourceType == typeof(float)) ||
                (sourceType == typeof(Vector2)) ||
                (sourceType == typeof(Vector4))
            )
                return true;

            if (typeof(IBezier).IsAssignableFrom(sourceType))
                return true;

            return false;
        }

        public override object ConvertTo (
            ITypeDescriptorContext context, CultureInfo culture, 
            object value, Type destinationType
        ) {
            if (!typeof(IParameter).IsAssignableFrom(destinationType))
                throw new InvalidCastException();

            var valueType = destinationType.GetGenericArguments()[0];
            var bezier = value as IBezier;

            if (valueType == typeof(float)) {
                if (bezier != null)
                    return new Parameter<float>((BezierF)value);
                else
                    return new Parameter<float>(Convert.ToSingle(value));
            } else if (valueType == typeof(Vector2)) {
                if (bezier != null)
                    return new Parameter<Vector2>((Bezier2)value);
                else
                    return new Parameter<Vector2>((Vector2)value);
            } else if (valueType == typeof(Vector4)) {
                if (bezier != null)
                    return new Parameter<Vector4>((Bezier4)value);
                else
                    return new Parameter<Vector4>((Vector4)value);
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
