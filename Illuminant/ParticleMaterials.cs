using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class ParticleMaterials {
        public readonly DefaultMaterialSet MaterialSet;

        public Material UpdatePositions, UpdateWithDistanceField;
        public Material FMA, Gravity, MatrixMultiply;
        public Material Spawn, NullTransform;

        public Material White, AttributeColor;

        internal ParticleMaterials (DefaultMaterialSet materialSet) {
            MaterialSet = materialSet;
        }
    }
}
