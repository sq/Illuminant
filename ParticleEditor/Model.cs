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

namespace ParticleEditor {
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

        public float Scale = 1;
    }

    public enum PresetChunkSize : int {
        Small = 32,
        Medium = 128,
        Large = 256
    }
}
