﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Illuminant.Particles;
using Squared.Util;

namespace Squared.Illuminant {
    public enum LightSourceTypeID : int {
        Unknown = 0,
        Sphere = 1,
        Directional = 2,
        Particle = 3,
        Line = 4,
        Projector = 5,
        Volumetric = 6,
        // Replicators are Sphere type
    }

    public enum ShadowFilter : int {
        None = -1,
        Unshadowed = 0,
        Shadowed = 1,
    }

    public enum VolumetricLightShape : int {
        Ellipsoid = 0,
        Cone = 1,
        // Capsules are just a less flexible version of the round cone shape we use... use a cone
        // Capsule = 2,
        Box = 2,
    }

    public abstract class LightSourceBase {
        [NonSerialized]
        public readonly LightSourceTypeID TypeID;

        [NonSerialized]
        public bool       Enabled = true;

        [NonSerialized]
        public object     UserData;

        /// <summary>
        /// Light sources are sorted by this value before being rendered.
        /// You can use this to create layers when using alternate blend modes.
        /// </summary>
        public int        SortKey;

        protected LightSourceBase (LightSourceTypeID typeID) {
            TypeID = typeID;
        }
    }

    public abstract class LightSource : LightSourceBase {
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

        internal float    AmbientOcclusionRadius = 0;
        internal float    AmbientOcclusionOpacity = 1;
        /// <summary>
        /// Allows you to scale the falloff of the light along the Y axis to fake foreshortening,
        ///  turning a spherical light into an ellipse. Isometric or 2.5D perspectives may look
        ///  better with this option adjusted.
        /// </summary>
        public float      FalloffYFactor = 1;
        /// <summary>
        /// Allows you to optionally set a ramp texture to control the appearance of light falloff.
        /// </summary>
        internal NullableLazyResource<Texture2D> TextureRef = new NullableLazyResource<Texture2D>();
        internal Vector2 RampOffsetAndRate = new Vector2(0, 1);
        /// <summary>
        /// Allows you to optionally override quality settings for this light.
        /// It is *much* faster to share a single settings instance for many lights!
        /// </summary>
        public RendererQualitySettings Quality = null;

        internal float RampOffsetForGPU => (float)-Math.PI + RampOffsetAndRate.X;
        internal float RampRateForGPU => (float)(1.0 / (Math.PI * 2) * RampOffsetAndRate.Y);

        protected LightSource (LightSourceTypeID typeID)
            : base (typeID) {
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
        /// <summary>
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        new public float AmbientOcclusionRadius {
            get {
                return base.AmbientOcclusionRadius;
            }
            set {
                base.AmbientOcclusionRadius = value;
            }
        }
        /// <summary>
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        new public float AmbientOcclusionOpacity {
            get {
                return base.AmbientOcclusionOpacity;
            }
            set {
                base.AmbientOcclusionOpacity = value;
            }
        }

        public ShadowFilter ShadowFilter = ShadowFilter.None;

        public DirectionalLightSource ()
            : base (LightSourceTypeID.Directional) {
        }

        public NullableLazyResource<Texture2D> RampTexture {
            get {
                return TextureRef;
            }
            set {
                TextureRef = value;
            }
        }

        public DirectionalLightSource Clone () {
            var result = new DirectionalLightSource {
                BlendMode = BlendMode,
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
                TextureRef = TextureRef,
                ShadowDistanceFalloff = ShadowDistanceFalloff,
                RampOffsetAndRate = RampOffsetAndRate,
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
        public Vector4 Color = Vector4.One;
        public Vector3 SpecularColor = Vector3.Zero;
        public float SpecularPower = 2.0f;
        /// <summary>
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        new public float AmbientOcclusionRadius {
            get {
                return base.AmbientOcclusionRadius;
            }
            set {
                base.AmbientOcclusionRadius = value;
            }
        }
        /// <summary>
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        new public float AmbientOcclusionOpacity {
            get {
                return base.AmbientOcclusionOpacity;
            }
            set {
                base.AmbientOcclusionOpacity = value;
            }
        }

        public ShadowFilter ShadowFilter = ShadowFilter.None;

        public SphereLightSource ()
            : base (LightSourceTypeID.Sphere) {
        }

        public NullableLazyResource<Texture2D> RampTexture {
            get {
                return TextureRef;
            }
            set {
                TextureRef = value;
            }
        }

        public float RampOffset {
            get { return RampOffsetAndRate.X; }
            set { RampOffsetAndRate.X = value; }
        }

        public float RampRate {
            get { return RampOffsetAndRate.Y; }
            set { RampOffsetAndRate.Y = value; }
        }

        public SphereLightSource Clone () {
            var result = new SphereLightSource {
                BlendMode = BlendMode,
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
                TextureRef = TextureRef,
                RampOffsetAndRate = RampOffsetAndRate,
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
                BlendMode = BlendMode,
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
                TextureRef = TextureRef,
                RampOffsetAndRate = RampOffsetAndRate,
            };
            return result;
        }
    }

    public class VolumetricLightSource : LightSource {
        public VolumetricLightShape Shape = VolumetricLightShape.Cone;
        /// <summary>
        /// The origin position of the volumetric light.
        /// </summary>
        public Vector3 StartPosition;
        /// <summary>
        /// The secondary position of the volumetric light.
        /// For cones, this is the endpoint of the cone.
        /// For ellipsoids and boxes, this is the light's radius.
        /// </summary>
        public Vector3 EndPosition;
        /// <summary>
        /// The direction the light is traveling for the purpose
        ///  of distance field shadow casting. If unset, the light
        ///  will automatically travel from the origin/center of the
        ///  light towards the point being lit.
        /// </summary>
        public Vector3? LightDirection;
        /// <summary>
        /// The start and end radiuses of the light, if applicable.
        /// </summary>
        public float   StartRadius, EndRadius = 0;
        /// <summary>
        /// Controls the brightness of each sampled point in the light volume.
        /// Lower values make the light brighter by requiring fewer samples.
        /// </summary>
        public float   Volumetricity = 1;
        /// <summary>
        /// Controls the distance at which a shaded pixel will receive
        ///  full diffuse light on contact, separate from the volume. 
        ///  1.0 is gradual falloff that ends exactly at the boundary.
        /// </summary>
        public float   DistanceAttenuation = 1.0f;
        /// <summary>
        /// Controls how far inside the volume a sample must be for
        ///  it to be fully illuminated, which creates a soft-edged volume.
        /// </summary>
        public float   RampLength = 1f;
        /// <summary>
        /// pow(distance / RampLength, RampPower)
        /// </summary>
        public float   RampPower = 1.0f;
        /// <summary>
        /// Amplifies diffuse directional highlights/shadows for pixels within 
        ///  the light volume to increase contrast/visibility.
        /// </summary>
        public float   BlowoutFactor = 0.0f;
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

        public VolumetricLightSource ()
            : base (LightSourceTypeID.Volumetric) {
        }

        public VolumetricLightSource Clone () {
            var result = new VolumetricLightSource {
                BlendMode = BlendMode,
                UserData = UserData,
                Shape = Shape,
                StartPosition = StartPosition,
                EndPosition = EndPosition,
                StartRadius = StartRadius,
                EndRadius = EndRadius,
                Volumetricity = Volumetricity,
                DistanceAttenuation = DistanceAttenuation,
                RampLength = RampLength,
                RampPower = RampPower,
                BlowoutFactor = BlowoutFactor,
                Color = Color,
                Opacity = Opacity,
                CastsShadows = CastsShadows,
                AmbientOcclusionRadius = AmbientOcclusionRadius,
                RampMode = RampMode,
                FalloffYFactor = FalloffYFactor,
                ShadowDistanceFalloff = ShadowDistanceFalloff,
                AmbientOcclusionOpacity = AmbientOcclusionOpacity,
                Quality = Quality,
                TextureRef = TextureRef,
                RampOffsetAndRate = RampOffsetAndRate,
            };
            return result;
        }
    }

    public class ParticleLightSource : LightSourceBase {
        public SphereLightSource Template = new SphereLightSource ();

        public Particles.ParticleSystem System;
        public bool IsActive = true;
        // Coarse-grained control over the number of particles that produce light. 
        // Defaults to the particle system's stipple factor.
        public float? StippleFactor = null;

        public ParticleLightSource ()
            : base (LightSourceTypeID.Particle) {
        }

        private ParticleRenderParameters _RenderParameters;

        internal ParticleRenderParameters RenderParameters {
            get {
                if (_RenderParameters?.StippleFactor != StippleFactor)
                    _RenderParameters = new ParticleRenderParameters { StippleFactor = StippleFactor };
                return _RenderParameters;
            }
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

    public class ProjectorLightSource : LightSource {
        public Matrix Transform = Matrix.Identity;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector2 Scale = Vector2.One;
        public Vector3 Position = Vector3.Zero;
        /// <summary>
        /// If set, the light is projected from the origin position and will be affected by
        ///  shadowing and surface normals.
        /// </summary>
        public Vector3? Origin = null;
        /// <summary>
        /// Configures the height of the projection (on the Z axis) in environment space.
        /// If unset, defaults to Environment.MaximumZ.
        /// </summary>
        public float? Depth = null;
        public Bounds TextureRegion = Bounds.Unit;
        public bool Wrap = true;

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
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        new public float AmbientOcclusionRadius {
            get {
                return base.AmbientOcclusionRadius;
            }
            set {
                base.AmbientOcclusionRadius = value;
            }
        }
        /// <summary>
        /// Uniformly obscures light if it is within N pixels of any obstacle. This produces
        ///  a 'blob shadow' around volumes within the distance field.
        /// </summary>
        new public float AmbientOcclusionOpacity {
            get {
                return base.AmbientOcclusionOpacity;
            }
            set {
                base.AmbientOcclusionOpacity = value;
            }
        }

        public ProjectorLightSource ()
            : base (LightSourceTypeID.Projector) {
        }

        public ProjectorLightSource Clone () {
            return new ProjectorLightSource {
                BlendMode = BlendMode,
                UserData = UserData,
                Transform = Transform,
                Rotation = Rotation,
                Scale = Scale,
                Position = Position,
                Wrap = Wrap,
                Opacity = Opacity,
                Depth = Depth,
                CastsShadows = CastsShadows,
                AmbientOcclusionRadius = AmbientOcclusionRadius,
                AmbientOcclusionOpacity = AmbientOcclusionOpacity,
                FalloffYFactor = FalloffYFactor,
                ShadowDistanceFalloff = ShadowDistanceFalloff,
                Quality = Quality,
                TextureRef = TextureRef,
                TextureRegion = TextureRegion,
                RampOffsetAndRate = RampOffsetAndRate,
            };
        }

        public NullableLazyResource<Texture2D> Texture {
            get {
                return TextureRef;
            }
            set {
                TextureRef = value;
            }
        }
    }

    public class LightSourceReplicator : LightSourceBase {
        public SphereLightSource Template = new SphereLightSource ();

        public UnorderedList<ReplicatedLight> Lights = new UnorderedList<ReplicatedLight>();

        public LightSourceReplicator ()
            : base(LightSourceTypeID.Sphere) {
        }

        public void Clear () => Lights.UnsafeFastClear();

        public void Add (ReplicatedLight light) => Lights.Add(ref light);
    }

    public struct ReplicatedLight {
        public Vector3 Position;
        public float? Radius, RampLength, SpecularPower, Opacity;
        public Vector4? Color;
        public Vector3? SpecularColor;
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
