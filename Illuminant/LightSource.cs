using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public abstract class LightSource {
        public object UserData;

        // The color of the light's illumination.
        // Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        // Alpha is *not* premultiplied (maybe it should be?)
        public Vector4 Color = Vector4.One;
        // A separate opacity factor that you can use to easily fade lights in/out.
        public float   Opacity = 1.0f;
        public bool    CastsShadows = true;
        // Uniformly obscures light if it is within N pixels of any obstacle.
        public float   AmbientOcclusionRadius = 0;
    }

    public class DirectionalLightSource : LightSource {
        /// <summary>
        /// The direction light travels.
        /// </summary>
        public Vector3 Direction;
        /// <summary>
        /// The distance in pixels that will be traced to find light obstructions.
        /// A larger value produces more accurate directional shadows at increased cost.
        /// </summary>
        public float   ShadowTraceLength = 64;
        /// <summary>
        /// Controls the maximum fuzziness of directional light shadows.
        /// </summary>
        public float   ShadowSoftness = 16;
        /// <summary>
        /// Controls how quickly directional light shadows become fuzzy.
        /// </summary>
        public float   ShadowRampRate = 0.05f;

        public DirectionalLightSource Clone () {
            var result = new DirectionalLightSource {
                UserData = UserData,
                Direction = Direction,
                ShadowTraceLength = ShadowTraceLength,
                ShadowSoftness = ShadowSoftness,
                ShadowRampRate = ShadowRampRate,
                Color = Color,
                Opacity = Opacity,
                CastsShadows = CastsShadows,
                AmbientOcclusionRadius = AmbientOcclusionRadius,
            };
            return result;
        }
    }

    public class SphereLightSource : LightSource {
        /// <summary>
        /// The center of the light source. 
        /// </summary>
        public Vector3 Position;
        /// <summary>
        /// The size of the light source.
        /// </summary>
        public float   Radius = 0;
        /// <summary>
        /// The size of the falloff around the light source.
        /// </summary>
        public float   RampLength = 1;
        /// <summary>
        /// Controls the nature of the light's distance falloff.
        /// Exponential produces falloff that is more realistic (square of distance or whatever) but not necessarily as expected. 
        /// </summary>
        public LightSourceRampMode RampMode = LightSourceRampMode.Linear;

        public Bounds3 Bounds {
            get {
                var size = new Vector3(Radius + RampLength);
                return new Bounds3(Position - size, Position + size);
            }
        }

        public SphereLightSource Clone () {
            var result = new SphereLightSource {
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
