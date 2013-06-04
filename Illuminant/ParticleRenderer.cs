using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Squared.Game;
using Squared.Render;

namespace Squared.Illuminant {
    public class ParticleRenderer {
        public readonly DefaultMaterialSet Materials;

        public readonly List<IParticleSystem> Systems = new List<IParticleSystem>();

        public Bounds Viewport;

        public ParticleRenderer (DefaultMaterialSet materials) {
            Materials = materials;
        }

        public void Draw (IBatchContainer container, int layer = 0) {
            foreach (var system in Systems)
                system.Draw(this, container, layer);
        }
    }
}
