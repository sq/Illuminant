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
        public bool Circular;

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