using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Squared.Illuminant.Configuration {
    public interface IParameter {
        Type ValueType { get; }
        bool IsConstant { get; }
        bool IsBezier { get; }
        IParameter ToBezier ();
        object Constant { get; }
        IBezier Bezier { get; }
    }

    [TypeConverter(typeof(ParameterConverter))]
    public struct Parameter<T> : IParameter
        where T : struct
    {
        private IBezier<T> _Bezier;
        private T _Constant;

        public Parameter (IBezier<T> bezier) {
            _Bezier = bezier;
            _Constant = default(T);
        }

        public Parameter (T value) {
            _Bezier = null;
            _Constant = value;
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
                        throw new InvalidOperationException("Parameter is not a constant");
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

        public Type ValueType {
            get {
                return typeof(T);
            }
        }

        public bool IsConstant {
            get {
                return (_Bezier == null) || _Bezier.IsConstant;
            }
        }

        public bool IsBezier {
            get {
                return (_Bezier != null);
            }
        }

        private bool TryConvertToBezier () {
            if (IsBezier)
                return true;

            switch (ValueType.Name) {
                case "Single":
                    _Bezier = (IBezier<T>)new BezierF(Convert.ToSingle(Constant));
                    return true;
                case "Vector2":
                    _Bezier = (IBezier<T>)new Bezier2((Vector2)Convert.ChangeType(Constant, ValueType));
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

        public Parameter<T> ToBezier () {
            var result = this;
            if (!IsBezier)
                if (!result.TryConvertToBezier())
                    throw new Exception();
            return result;
        }

        IParameter IParameter.ToBezier () {
            return ToBezier();
        }

        IBezier IParameter.Bezier {
            get {
                return _Bezier;
            }
        }

        object IParameter.Constant {
            get {
                return Constant;
            }
        }

        public T Evaluate (float t) {
            if (_Bezier != null)
                return _Bezier.Evaluate(t);
            else
                return _Constant;
        }

        public static implicit operator Parameter<T> (T value) {
            return new Parameter<T>(value);
        }
    }

    public struct DynamicMatrix {
        public static readonly DynamicMatrix Identity = new DynamicMatrix(Matrix.Identity);

        public bool   IsGenerated;
        public float  Angle;
        public float  Scale;
        public Matrix Matrix;

        public DynamicMatrix (Matrix matrix) {
            Matrix = matrix;
            Scale = 1;
            Angle = 0;
            IsGenerated = false;
        }

        public void Regenerate () {
            if (!IsGenerated)
                return;

            var rot = Matrix.CreateRotationZ(MathHelper.ToRadians(Angle));
            Matrix.Multiply(ref rot, Scale, out Matrix);
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
