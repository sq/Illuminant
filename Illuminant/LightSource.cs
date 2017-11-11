using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public enum LightSourceTypeID : int {
        Unknown = 0,
        Sphere = 1,
        Directional = 2
    }

    public abstract class LightSource {
        public readonly LightSourceTypeID TypeID;

        public object UserData;

        // The color of the light's illumination.
        // Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        // Alpha is *not* premultiplied (maybe it should be?)
        public Vector4   Color = Vector4.One;
        // A separate opacity factor that you can use to easily fade lights in/out.
        public float     Opacity = 1.0f;
        public bool      CastsShadows = true;
        // FIXME: Not implemented in shaders
        public float?    ShadowDistanceFalloff = null;
        // Uniformly obscures light if it is within N pixels of any obstacle.
        public float     AmbientOcclusionRadius = 0;
        // Allows you to scale the falloff of the light along the Y axis to fake foreshortening.
        public float     FalloffYFactor = 1;
        // Allows you to optionally set a ramp texture to control the appearance of light falloff
        public Texture2D RampTexture = null;


        protected LightSource (LightSourceTypeID typeID) {
            TypeID = typeID;
        }
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
        public float   ShadowTraceLength = 256;
        /// <summary>
        /// Controls the maximum fuzziness of directional light shadows.
        /// </summary>
        public float   ShadowSoftness = 12;
        /// <summary>
        /// Controls how quickly directional light shadows become fuzzy.
        /// </summary>
        public float   ShadowRampRate = 0.5f;
        /// <summary>
        /// Controls the length of the shadow softness ramp.
        /// </summary>
        public float   ShadowRampLength = 256f;

        public DirectionalLightSource ()
            : base (LightSourceTypeID.Directional) {
        }

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
        /// <summary>
        /// If using a ramp texture, this selects values from the ramp based on distance from light instead of light brightness.
        /// Non-linear ramps (with weird patterns or what have you) will look really weird unless you set this to true.
        /// </summary>
        public bool UseDistanceForRampTexture = false;

        public Bounds3 Bounds {
            get {
                var size = new Vector3(Radius + RampLength);
                return new Bounds3(Position - size, Position + size);
            }
        }

        public SphereLightSource ()
            : base (LightSourceTypeID.Sphere) {
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
                RampMode = RampMode,
                FalloffYFactor = FalloffYFactor,
                ShadowDistanceFalloff = ShadowDistanceFalloff
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
