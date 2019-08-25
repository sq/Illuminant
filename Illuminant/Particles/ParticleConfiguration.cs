using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Illuminant.Particles {
    public class ParticleCollision {
        /// <summary>
        /// If set, particles collide with volumes in this distance field
        /// </summary>
        [NonSerialized]
        public DistanceField DistanceField;
        [NonSerialized]
        public float?        DistanceFieldMaximumZ;

        /// <summary>
        /// The distance at which a particle is considered colliding with the field.
        /// Raise this to make particles 'larger'.
        /// </summary>
        public float         Distance = 0.33f;

        /// <summary>
        /// Life of a particle decreases by this much every frame if it collides
        ///  with or is inside of a volume
        /// </summary>
        public float         LifePenalty = 0;

        /// <summary>
        /// Particles trapped inside distance field volumes will attempt to escape
        ///  at this velocity multiplied by their distance from the outside
        /// </summary>
        public float         EscapeVelocity = 128.0f;

        /// <summary>
        /// Particles colliding with distance field volumes will retain this much
        ///  of their speed and bounce off of the volume
        /// </summary>
        public float         BounceVelocityMultiplier = 0.0f;
    }

    public class ParticleAppearance {
        public ParticleAppearance (Texture2D texture = null, string textureName = null) {
            Texture.Set(texture, textureName);
        }

        /// <summary>
        /// Configures the sprite used to render each particle.
        /// If null, the particle will be a solid-color quad
        /// </summary>
        public NullableLazyResource<Texture2D> Texture = new NullableLazyResource<Texture2D>();

        /// <summary>
        /// The offset into the texture at which the first frame of the sprite's animation begins.
        /// </summary>
        public Vector2 OffsetPx;

        /// <summary>
        /// The size of the section of the texture used by the particle (the whole texture is used by default).
        /// </summary>
        public Vector2? SizePx;

        /// <summary>
        /// Animates through the sprite texture based on the particle's life value, if set
        /// Smaller values will result in slower animation. Zero turns off animation.
        /// </summary>
        public Vector2 AnimationRate;

        /// <summary>
        /// Rounds the corners of the displayed particle (regardless of whether it has a texture).
        /// </summary>
        public bool Rounded;

        /// <summary>
        /// When particles fade out that will be done via dithering instead of opacity.
        /// </summary>
        public bool DitheredOpacity;

        /// <summary>
        /// Applies a gamma curve to the opacity of circular particles
        /// </summary>
        public BezierF RoundingPowerFromLife = new BezierF(0.8f);

        /// <summary>
        /// Renders textured particles with bilinear filtering.
        /// </summary>
        public bool Bilinear = true;

        /// <summary>
        /// If true, the size of particles is relative to the size of their sprite texture.
        /// </summary>
        public bool RelativeSize = true;

        /// <summary>
        /// If true, the texture is treated as a spritesheet with each row representing a different angle of rotation.
        /// </summary>
        public bool RowFromVelocity = false;
        /// <summary>
        /// If true, the texture is treated as a spritesheet with each column representing a different angle of rotation.
        /// </summary>
        public bool ColumnFromVelocity = false;

        public Rectangle Rectangle {
            set {
                OffsetPx = new Vector2(value.X, value.Y);
                SizePx = new Vector2(value.Width, value.Height);
            }
        }
    }

    public class ParticleColorLifeRamp {
        /// <summary>
        /// Life values below this are treated as zero
        /// </summary>
        public float Minimum = 0.0f;

        /// <summary>
        /// Life values above this are treated as one
        /// </summary>
        public float Maximum = 100f;

        /// <summary>
        /// Blends between the constant color value for the particle and the color
        ///  from its life ramp
        /// </summary>
        public float Strength = 1.0f;

        /// <summary>
        /// If set, the life ramp has its maximum value at the left instead of the right.
        /// </summary>
        public bool  Invert;

        /// <summary>
        /// Specifies a color ramp texture
        /// </summary>
        public NullableLazyResource<Texture2D> Texture;
    }

    public class ParticleColor {
        internal Bezier4  _ColorFromLife = null;
        internal float?   _OpacityFromLife = null;

        /// <summary>
        /// Sets a global multiply color to apply to the particles
        /// </summary>
        public Vector4    Global = Vector4.One;

        public Bezier4 ColorFromVelocity = null;

        public ParticleColorLifeRamp LifeRamp;

        /// <summary>
        /// Multiplies the particle's opacity, producing a fade-in or fade-out based on the particle's life
        /// </summary>
        public float? OpacityFromLife {
            set {
                if (value == _OpacityFromLife)
                    return;

                _OpacityFromLife = value;
                if (value != null)
                    _ColorFromLife = null;
            }
            get {
                return _OpacityFromLife;
            }
        }

        /// <summary>
        /// Multiplies the particle's color, producing a fade-in or fade-out based on the particle's life
        /// </summary>
        public Bezier4 FromLife {
            get {
                return _ColorFromLife;
            }
            set {
                if (value == _ColorFromLife)
                    return;

                _ColorFromLife = value;
                if (value != null)
                    _OpacityFromLife = null;
            }
        }
    }

    public class ParticleSystemConfiguration {
        /// <summary>
        /// Used to measure elapsed time automatically for updates
        /// </summary>
        [NonSerialized]
        public ITimeProvider TimeProvider = null;

        /// <summary>
        /// Configures the texture used when drawing particles (if any)
        /// </summary>
        public ParticleAppearance Appearance = new ParticleAppearance();

        /// <summary>
        /// Configures the color of particles
        /// </summary>
        public ParticleColor Color = new ParticleColor();

        /// <summary>
        /// The on-screen size of each particle, in pixels
        /// </summary>
        public Vector2       Size = Vector2.One;

        /// <summary>
        /// Multiplies the particle's size, producing a shrink or grow based on the particle's life
        /// </summary>
        public BezierF       SizeFromLife = null;

        /// <summary>
        /// Multiplies the particle's size, producing a shrink or grow based on the speed of the particle
        /// </summary>
        public BezierF       SizeFromVelocity = null;

        /// <summary>
        /// Life of all particles decreases by this much every second
        /// </summary>
        public float         LifeDecayPerSecond = 1;

        /// <summary>
        /// Configures collision detection for particles
        /// </summary>
        public ParticleCollision Collision = new ParticleCollision(); 

        /// <summary>
        /// Particles will not be allowed to exceed this velocity
        /// </summary>
        public float         MaximumVelocity = 9999f;

        /// <summary>
        /// All particles will have their velocity reduced to roughly Velocity * (1.0 - Friction) every second
        /// </summary>
        public float         Friction = 0f;

        /// <summary>
        /// Applies the particle's Z coordinate to its Y coordinate at render time for 2.5D effect
        /// </summary>
        public float         ZToY = 0;

        /// <summary>
        /// Coarse-grained control over the number of particles actually rendered
        /// </summary>
        [NonSerialized]
        public float         StippleFactor = 1.0f;

        /// <summary>
        /// If set, particles will rotate based on their direction of movement
        /// </summary>
        public bool          RotationFromVelocity;

        /// <summary>
        /// Makes particles spin based on their life value
        /// </summary>
        public float         RotationFromLife = 0;

        /// <summary>
        /// Gives particles a constant rotation based on their index (pseudorandom-ish)
        /// </summary>
        public float         RotationFromIndex = 0;

        /// <summary>
        /// If set, the system's state will automatically be read into system memory after
        ///  every update
        /// </summary>
        [NonSerialized]
        public bool          AutoReadback = false;

        /// <summary>
        /// If set, the bitmap list created by readback will be sorted by particle's Z and Y values
        /// </summary>
        public bool          SortedReadback = true;

        /// <summary>
        /// Specifies the contribution of a particle's x, y, and z position elements to the Z value of
        ///  the rasterized particle. This can be combined with depth testing to cull particles against
        ///  other scene elements.
        /// The formula is scaled by 1000 to reduce error.
        /// </summary>
        public Vector4       ZFormulaTimes1000 = Vector4.Zero;

        /// <summary>
        /// Specifies a depth/stencil mode to use for rasterizing particles.
        /// You can use this to depth test or stencil test particles.
        /// </summary>
        [NonSerialized]
        public DepthStencilState DepthStencilState = DepthStencilState.None;

        public ParticleSystemConfiguration () {
        }

        public ParticleSystemConfiguration Clone () {
            var result = (ParticleSystemConfiguration)this.MemberwiseClone();
            return result;
        }
    }

    public class ParticleRenderParameters {
        public Vector2 Origin = Vector2.Zero;
        public Vector2 Scale = Vector2.One;
        public float? StippleFactor = null;
    }
}
