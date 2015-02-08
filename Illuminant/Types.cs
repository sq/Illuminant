using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace Squared.Illuminant {
    public struct LightPosition {
        private Vector3 _Position;

        public LightPosition (float x, float y, float z = 0) {
            _Position = new Vector3(x, y, z);
        }

        public LightPosition (Vector3 v) {
            _Position = v;
        }

        public float X {
            get {
                return _Position.X;
            }
        }

        public float Y {
            get {
                return _Position.Y;
            }
        }

        public float Z {
            get {
                return _Position.Z;
            }
        }

        public static explicit operator Vector2 (LightPosition lp) {
            return new Vector2(lp._Position.X, lp._Position.Y);
        }

        public static implicit operator Vector3 (LightPosition lp) {
            return lp._Position;
        }

        public static implicit operator LightPosition (Vector2 v) {
            return new LightPosition { _Position = new Vector3(v, 0) };
        }

        public static implicit operator LightPosition (Vector3 v) {
            return new LightPosition { _Position = v };
        }
    }
}
