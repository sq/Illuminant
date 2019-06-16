using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Illuminant.Configuration;

namespace Squared.Illuminant {
    public interface IFormula {
        IParameter Constant { get; }
        IParameter RandomScale { get; }
        IParameter Offset { get; }
        FormulaType? Type { get; }
    }

    public class Formula1 : IFormula {
        public Parameter<float> Constant;
        public Parameter<float> RandomScale;
        public Parameter<float> Offset;

        FormulaType? IFormula.Type {
            get {
                return null;
            }
        }

        IParameter IFormula.Constant {
            get {
                return Constant;
            }
        }

        IParameter IFormula.RandomScale {
            get {
                return RandomScale;
            }
        }

        IParameter IFormula.Offset {
            get {
                return Offset;
            }
        }

        public void SetToUnitNormal () {
            Constant = 0;
            Offset = -0.5f;
            RandomScale = 1f;
        }

        public void SetToConstant (float value) {
            Constant = value;
            Offset = RandomScale = 0;
        }

        public static Formula1 FromConstant (float value) {
            var result = new Formula1();
            result.SetToConstant(value);
            return result;
        }

        public static Formula1 UnitNormal () {
            var result = new Formula1();
            result.SetToUnitNormal();
            return result;
        }

        public static Formula1 Zero () {
            return new Formula1();
        }

        public static Formula1 One () {
            return new Formula1 {
                Constant = 1
            };
        }

        public Formula1 Clone () {
            return (Formula1)MemberwiseClone();
        }
    }

    public class Formula3 : IFormula {
        public Parameter<Vector3> Constant;
        public Parameter<Vector3> RandomScale;
        public Parameter<Vector3> Offset;
        public FormulaType Type;

        FormulaType? IFormula.Type {
            get {
                return Type;
            }
        }

        IParameter IFormula.Constant {
            get {
                return Constant;
            }
        }

        IParameter IFormula.RandomScale {
            get {
                return RandomScale;
            }
        }

        IParameter IFormula.Offset {
            get {
                return Offset;
            }
        }

        internal bool Circular {
            get {
                return Type == FormulaType.Spherical;
            }
            set {
                if (value == false)
                    return;
                Type = FormulaType.Spherical;
            }
        }

        public Parameter<Vector3> RandomOffset {
            set {
                if (Circular)
                    return;
                Offset = value;
            }
        }

        public Parameter<Vector3> ConstantRadius {
            set {
                if (!Circular)
                    return;
                Offset = value;
            }
        }

        public void SetToUnitNormal () {
            Constant = Vector3.Zero;
            Offset = Vector3.Zero;
            RandomScale = Vector3.One;
            Circular = true;
        }

        public void SetToConstant (Vector3 value) {
            Constant = value;
            Offset = RandomScale = Vector3.Zero;
            Circular = false;
        }

        public static Formula3 Towards (Vector3 point, float randomSpeed, float constantSpeed) {
            var result = new Formula3();
            result.Constant = point;
            result.RandomScale = new Vector3(randomSpeed);
            result.ConstantRadius = new Vector3(constantSpeed);
            result.Type = FormulaType.Towards;
            return result;
        }

        public static Formula3 FromConstant (Vector3 value) {
            var result = new Formula3();
            result.SetToConstant(value);
            return result;
        }

        public static Formula3 UnitNormal () {
            var result = new Formula3();
            result.SetToUnitNormal();
            return result;
        }

        public static Formula3 Zero () {
            return new Formula3();
        }

        public static Formula3 One () {
            return new Formula3 {
                Constant = Vector3.One
            };
        }

        public Formula3 Clone () {
            return (Formula3)MemberwiseClone();
        }
    }

    public class Formula4 : IFormula {
        public Parameter<Vector4> Constant;
        public Parameter<Vector4> RandomScale;
        public Parameter<Vector4> Offset;

        FormulaType? IFormula.Type {
            get {
                return null;
            }
        }

        IParameter IFormula.Constant {
            get {
                return Constant;
            }
        }

        IParameter IFormula.RandomScale {
            get {
                return RandomScale;
            }
        }

        IParameter IFormula.Offset {
            get {
                return Offset;
            }
        }

        public void SetToConstant (Vector4 value) {
            Constant = value;
            Offset = RandomScale = Vector4.Zero;
        }

        public static Formula4 FromConstant (Vector4 value) {
            var result = new Formula4();
            result.SetToConstant(value);
            return result;
        }

        public static Formula4 Zero () {
            return new Formula4();
        }

        public static Formula4 One () {
            return new Formula4 {
                Constant = Vector4.One
            };
        }

        public Formula4 Clone () {
            return (Formula4)MemberwiseClone();
        }
    }

    public enum FormulaType : uint {
        Linear = 0,
        Spherical = 1,
        Towards = 2,
        Rectangular = 3
    }
}