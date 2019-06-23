using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class ParticleMaterials {
        public readonly DefaultMaterialSet MaterialSet;

        public Material Erase, UpdatePositions, UpdateWithDistanceField;
        public Material FMA, Gravity, MatrixMultiply, Noise, SpatialNoise;
        public Material Spawn, SpawnFromPositionTexture, SpawnFeedback, SpawnPattern;
        public Material CountLiveParticles, CollectParticles, CountLiveParticlesFast;

        public Material TextureLinear, TexturePoint, NoTexture;

        public DepthStencilState CountDepthStencilState;

        public bool IsLoaded { get; internal set; }

        public ParticleMaterials (DefaultMaterialSet materialSet) {
            MaterialSet = materialSet;
        }
    }
}
