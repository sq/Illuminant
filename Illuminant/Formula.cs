using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Illuminant.Configuration;

namespace Squared.Illuminant {
    public class Formula {
        public Parameter<Vector4> Constant;
        public Parameter<Vector4> RandomScale;
        public Parameter<Vector4> Offset;
        public FormulaType Type;

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

        public Parameter<Vector4> RandomOffset {
            set {
                if (Circular)
                    return;
                Offset = value;
            }
        }

        public Parameter<Vector4> ConstantRadius {
            set {
                if (!Circular)
                    return;
                Offset = value;
            }
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

        public static Formula Towards (Vector3 point, float life, float randomSpeed, float constantSpeed) {
            var result = new Formula();
            result.Constant = new Vector4(point, life);
            result.RandomScale = new Vector4(randomSpeed, randomSpeed, randomSpeed, 0);
            result.ConstantRadius = new Vector4(constantSpeed, constantSpeed, constantSpeed, 0);
            result.Type = FormulaType.Towards;
            return result;
        }

        public static Formula FromConstant (Vector4 value) {
            var result = new Formula();
            result.SetToConstant(value);
            return result;
        }

        public static Formula UnitNormal () {
            var result = new Formula();
            result.SetToUnitNormal();
            return result;
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

    public enum FormulaType : uint {
        Linear = 0,
        Spherical = 1,
        Towards = 2
    }
}