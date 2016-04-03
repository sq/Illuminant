using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightSource {
        public object UserData;

        // The center of the light source.
        public Vector3 Position;
        // The size of the light source.
        public float   Radius = 0;
        // The size of the falloff around the light source.
        public float   RampLength = 1;
        // The color of the light's illumination.
        // Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        // Alpha is *not* premultiplied (maybe it should be?)
        public Vector4 Color = Vector4.One;
        // A separate opacity factor that you can use to easily fade lights in/out.
        public float   Opacity = 1.0f;
        public bool    CastsShadows = true;
        // Uniformly obscures light if it is within N pixels of any obstacle.
        public float   AmbientOcclusionRadius = 0;
        // Controls the nature of the light's distance falloff. Exponential produces falloff that is more realistic (square of distance or whatever) but not necessarily as expected.
        public LightSourceRampMode RampMode = LightSourceRampMode.Linear;

        public Bounds3 Bounds {
            get {
                var size = new Vector3(Radius + RampLength);
                return new Bounds3(Position - size, Position + size);
            }
        }

        public LightSource Clone () {
            var result = new LightSource {
                UserData = UserData,
                Position = Position,
                Radius = Radius,
                RampLength = RampLength,
                Color = Color,
                Opacity = Opacity,
                CastsShadows = CastsShadows,
                AmbientOcclusionRadius = AmbientOcclusionRadius,
                RampMode = RampMode
            };
            return result;
        }
    }

    public enum LightSourceRampMode {
        // Linear falloff once outside radius
        Linear,
        // Exponential falloff once outside radius
        Exponential,
        // Constant full brightness within radius
        None
    }
}
