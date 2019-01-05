using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant;

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
    }
}
