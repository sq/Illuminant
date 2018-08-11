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
        Directional = 2,
        Particle = 3
    }

    public abstract class LightSource {
        public readonly LightSourceTypeID TypeID;

        public object UserData;

        /// <summary>
        /// The color of the light's illumination.
        /// Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        /// Alpha is *not* premultiplied (maybe it should be?)
        /// </summary>
        public Vector4   Color = Vector4.One;
        /// <summary>
        /// A separate opacity factor that you can use to easily fade lights in/out.
        /// </summary>
        public float     Opacity = 1.0f;
        /// <summary>
        /// If set, volumes within the distance field will obstruct light from this light source,
        ///  creating shadows. Otherwise, any objects facing this light will be lit (if in range).
        /// </summary>
        public bool      CastsShadows = true;
        // FIXME: Not implemented in shaders
        public float?    ShadowDistanceFalloff = null;
        /// <summary>
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        public float     AmbientOcclusionRadius = 0;
        public float     AmbientOcclusionOpacity = 1;
        /// <summary>
        /// Allows you to scale the falloff of the light along the Y axis to fake foreshortening,
        ///  turning a spherical light into an ellipse. Isometric or 2.5D perspectives may look
        ///  better with this option adjusted.
        /// </summary>
        public float     FalloffYFactor = 1;
        /// <summary>
        /// Allows you to optionally set a ramp texture to control the appearance of light falloff.
        /// </summary>
        public Texture2D RampTexture = null;
        /// <summary>
        /// Allows you to optionally override quality settings for this light.
        /// It is *much* faster to share a single settings instance for many lights!
        /// </summary>
        public RendererQualitySettings Quality = null;


        protected LightSource (LightSourceTypeID typeID) {
            TypeID = typeID;
        }
    }

    public class DirectionalLightSource : LightSource {
        internal Vector3 _Direction;

        /// <summary>
        /// The direction light travels.
        /// </summary>
        public Vector3 Direction {
            get {
                return _Direction;
            }
            set {
                value.Normalize();
                _Direction = value;
            }
        }
        /// <summary>
        /// The distance in pixels that will be traced to find light obstructions.
        /// A larger value produces more accurate directional shadows at increased cost
        ///  because a directional light's light source is an infinite distance away.
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

        public DirectionalLightSource ()
            : base (LightSourceTypeID.Directional) {
        }

        public DirectionalLightSource Clone () {
            var result = new DirectionalLightSource {
                UserData = UserData,
                _Direction = _Direction,
                ShadowTraceLength = ShadowTraceLength,
                ShadowSoftness = ShadowSoftness,
                ShadowRampRate = ShadowRampRate,
                Color = Color,
                Opacity = Opacity,
                CastsShadows = CastsShadows,
                AmbientOcclusionRadius = AmbientOcclusionRadius,
                AmbientOcclusionOpacity = AmbientOcclusionOpacity,
                Quality = Quality,
                FalloffYFactor = FalloffYFactor,
                RampTexture = RampTexture,
                ShadowDistanceFalloff = ShadowDistanceFalloff
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
                ShadowDistanceFalloff = ShadowDistanceFalloff,
                AmbientOcclusionOpacity = AmbientOcclusionOpacity,
                Quality = Quality,
                RampTexture = RampTexture,
                UseDistanceForRampTexture = UseDistanceForRampTexture
            };
            return result;
        }
    }

    public class ParticleLightSource : LightSource {
        public SphereLightSource Template = new SphereLightSource ();

        public Particles.ParticleSystem System;
        public bool IsActive = true;
        // Coarse-grained control over the number of particles that produce light. 
        // Defaults to the particle system's stipple factor.
        public float? StippleFactor = null;

        public ParticleLightSource ()
            : base (LightSourceTypeID.Particle) {
        }

        public ParticleLightSource Clone (bool deep) {
            var result = new ParticleLightSource {
                Template = deep ? Template.Clone() : Template,
                System = System,
                IsActive = IsActive,
                StippleFactor = StippleFactor
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
