using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class ParticleMaterials {
        public readonly DefaultMaterialSet MaterialSet;

        public Material UpdateParticleSystem;

        internal ParticleMaterials (DefaultMaterialSet materialSet) {
            MaterialSet = materialSet;
        }
    }
}
