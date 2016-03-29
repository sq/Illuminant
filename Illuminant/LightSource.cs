using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightSource : IHasBounds {
        public object UserData;

        private LightPosition _Position;
        private Bounds3 _Bounds;
        private float _RampLength = 1;

        public LightPosition Position {
            get {
                return _Position;
            }
            set {
                _Position = value;
                UpdateBounds();
            }
        }

        public Bounds3 Bounds {
            get {
                return _Bounds;
            }
        }

        public float RampLength {
            get {
                return _RampLength;
            }
            set {
                _RampLength = value;
                UpdateBounds();
            }
        }

        public LightSource () {
            UpdateBounds();
        }

        // Controls the blending mode used to render the light source. Additive is usually what you want.
        public LightSourceMode Mode = LightSourceMode.Additive;
        // Controls the nature of the light's distance falloff. Exponential produces falloff that is more realistic (square of distance or whatever) but not necessarily as expected.
        public LightSourceRampMode RampMode = LightSourceRampMode.Linear;
        // The color of the light's illumination.
        // Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        // Alpha is *not* premultiplied (maybe it should be?)
        public Vector4 Color = Vector4.One;
        // The size of the light source.
        public float Radius = 0;
        // A separate opacity factor that you can use to easily fade lights in/out.
        public float Opacity = 1.0f;

        void UpdateBounds () {
            var sz = (Vector3.One * (Radius + RampLength));
            _Bounds = new Bounds3(
                _Position - sz,
                _Position + sz
            );
        }

        Bounds IHasBounds.Bounds {
            get {
                return _Bounds.XY;
            }
        }
    }

    public enum LightSourceRampMode {
        Linear,
        Exponential
    }

    public enum LightSourceMode {
        Additive,
        Subtractive,
        Alpha,
        Max,
        Min
    }
}
