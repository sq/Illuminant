using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Configuration;

namespace Lumined {
    public class EditorData {
        /// <summary>
        /// Sets a fixed framerate for all systems in the scene.
        /// </summary>
        public int? FrameRate;

        /// <summary>
        /// Limits updates to advancing time by a certain amount.
        /// </summary>
        public int MaximumDeltaTimeMS = 500;

        /// <summary>
        /// Sets a background image for the scene.
        /// </summary>
        public NullableLazyResource<Texture2D> Background;

        /// <summary>
        /// Sets a background color for the scene.
        /// </summary>
        public Color BackgroundColor = Color.Black;

        /// <summary>
        /// A list of sprites to draw on top of the scene background.
        /// </summary>
        public readonly List<EditorSprite> Sprites = new List<EditorSprite>();

        /// <summary>
        /// A list of light sources to render in the preview scene.
        /// </summary>
        public readonly List<EditorLight> Lights = new List<EditorLight>();

        /// <summary>
        /// Determines the size of the particle buffers used by the particle systems.
        /// A small size is ideal for scenarios where you are not spawning many particles,
        ///  or where you need to read particle state from the GPU.
        /// </summary>
        public PresetChunkSize ChunkSize = PresetChunkSize.Large;

        /// <summary>
        /// If true, particles are drawn as bitmaps that are configured on-CPU.
        /// The bitmaps can be sorted into lists of other bitmaps and can use custom materials.
        /// </summary>
        public bool DrawAsBitmaps = false;

        /// <summary>
        /// If true, total particles will be accurately counted. Otherwise an efficient count will be used that just determines whether a chunk is empty.
        /// </summary>
        public bool AccurateCounting = true;

        /// <summary>
        /// Runs the preview at a fixed pace even if the framerate is low. This produces consistent results.
        /// </summary>
        public bool FixedTimeStep = true;

        /// <summary>
        /// If true, particles will write into the depth buffer and not overdraw. This will look wrong with transparency.
        /// </summary>
        public bool DepthWrite = false;

        /// <summary>
        /// Specifies where to search for texture resources.
        /// </summary>
        public DirectoryName ResourceDirectory = new DirectoryName();
    }

    public class DirectoryName {
        public string Path = null;
    }

    public class EditorSprite {
        /// <summary>
        /// The sprite image.
        /// </summary>
        public NullableLazyResource<Texture2D> Texture;

        /// <summary>
        /// The center point of the sprite in the scene.
        /// </summary>
        public Parameter<Vector3> Location;

        /// <summary>
        /// The top left corner of the sprite's region in the texture.
        /// </summary>
        public Vector2? TextureTopLeftPx;

        /// <summary>
        /// The size of the sprite's region in the texture.
        /// </summary>
        public Vector2? TextureSizePx;

        /// <summary>
        /// Writes a value to the depth buffer.
        /// </summary>
        public float? Z;

        public float Scale = 1;
    }

    public class EditorLight {
        /// <summary>
        /// If set, this is a particle light source.
        /// </summary>
        public ParticleSystemReference ParticleSystem;
        public float ParticleStippleFactor = 1.0f;

        public Vector3 WorldPosition;

        public float Radius = 16;
        public float Falloff = 128;

        public Color Color = Color.White;
    }

    public enum PresetChunkSize : int {
        Small = 32,
        Medium = 128,
        Large = 256
    }
}
