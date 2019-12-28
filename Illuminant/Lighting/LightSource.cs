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
        Particle = 3,
        Line = 4,
    }

    public abstract class LightSource {
        [NonSerialized]
        public readonly LightSourceTypeID TypeID;

        public object     UserData;

        /// <summary>
        /// Light sources are sorted by this value before being rendered.
        /// You can use this to create layers when using alternate blend modes.
        /// </summary>
        public int        SortKey;

        /// <summary>
        /// Overrides how light is blended into the lightmap.
        /// The default is an additive blend, use null for that.
        /// Mixing blend modes will cause lights to be sorted into groups by mode,
        ///  if you want them to be painted in a particular order use SortKey.
        /// </summary>
        public BlendState BlendMode;

        /// <summary>
        /// A separate opacity factor that you can use to easily fade lights in/out.
        /// </summary>
        public float      Opacity = 1.0f;
        /// <summary>
        /// If set, volumes within the distance field will obstruct light from this light source,
        ///  creating shadows. Otherwise, any objects facing this light will be lit (if in range).
        /// </summary>
        public bool       CastsShadows = true;
        public float?     ShadowDistanceFalloff = null;
        /// <summary>
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        public float      AmbientOcclusionRadius = 0;
        public float      AmbientOcclusionOpacity = 1;
        /// <summary>
        /// Allows you to scale the falloff of the light along the Y axis to fake foreshortening,
        ///  turning a spherical light into an ellipse. Isometric or 2.5D perspectives may look
        ///  better with this option adjusted.
        /// </summary>
        public float      FalloffYFactor = 1;
        /// <summary>
        /// Allows you to optionally set a ramp texture to control the appearance of light falloff.
        /// </summary>
        public NullableLazyResource<Texture2D> RampTexture = new NullableLazyResource<Texture2D>();
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
        internal Vector3? _Direction;

        /// <summary>
        /// Allows restricting the directional light to a subset of the world.
        /// You can combine this with a null Direction to create an ambient light in an area.
        /// </summary>
        public Bounds? Bounds;
        /// <summary>
        /// The direction light travels.
        /// Because I'm lazy you can set the direction to null in order to have a nondirectional ambient light.
        /// </summary>
        public Vector3? Direction {
            get {
                return _Direction;
            }
            set {
                if (value.HasValue) {
                    var dir = value.Value;
                    dir.Normalize();
                    _Direction = dir;
                } else {
                    _Direction = null;
                }
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
        /// <summary>
        /// The color of the light's illumination.
        /// Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        /// Alpha is *not* premultiplied (maybe it should be?)
        /// </summary>
        public Vector4   Color = Vector4.One;

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
        /// The color of the light's illumination.
        /// Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        /// Alpha is *not* premultiplied (maybe it should be?)
        /// </summary>
        public Vector4   Color = Vector4.One;

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
            };
            return result;
        }
    }

    public class LineLightSource : LightSource {
        /// <summary>
        /// The position of the beginning of the line.
        /// </summary>
        public Vector3 StartPosition;
        /// <summary>
        /// The position of the end of the line.
        /// </summary>
        public Vector3 EndPosition;
        /// <summary>
        /// The size of the light source.
        /// </summary>
        public float   Radius = 0;
        /// <summary>
        /// Controls the nature of the light's distance falloff.
        /// Exponential produces falloff that is more realistic (square of distance or whatever) but not necessarily as expected. 
        /// </summary>
        public LightSourceRampMode RampMode = LightSourceRampMode.Linear;
        /// <summary>
        /// The color of the light's illumination.
        /// Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        /// Alpha is *not* premultiplied (maybe it should be?)
        /// </summary>
        public Vector4   StartColor = Vector4.One, EndColor = Vector4.One;

        public Vector4 Color {
            set {
                StartColor = EndColor = value;
            }
        }

        public LineLightSource ()
            : base (LightSourceTypeID.Line) {
        }

        public LineLightSource Clone () {
            var result = new LineLightSource {
                UserData = UserData,
                StartPosition = StartPosition,
                EndPosition = EndPosition,
                Radius = Radius,
                StartColor = StartColor,
                EndColor = EndColor,
                Opacity = Opacity,
                CastsShadows = CastsShadows,
                AmbientOcclusionRadius = AmbientOcclusionRadius,
                RampMode = RampMode,
                FalloffYFactor = FalloffYFactor,
                ShadowDistanceFalloff = ShadowDistanceFalloff,
                AmbientOcclusionOpacity = AmbientOcclusionOpacity,
                Quality = Quality,
                RampTexture = RampTexture,
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

        internal int GetUniqueId () {
            if (System == null)
                return 0;
            else
                return System.GetHashCode();
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
