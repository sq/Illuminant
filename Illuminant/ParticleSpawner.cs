using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Transforms;
using Squared.Illuminant.Uniforms;
using Squared.Illuminant.Util;
using Squared.Render;

namespace Squared.Illuminant.Transforms {
    public class Spawner : ParticleTransform {
        public float    MinInterval, MaxInterval;
        public float    MinCount, MaxCount;

        public Formula  Position, Velocity, Attributes;

        private UniformBinding<SpawnerUniforms> Uniforms;

        private int     Delay;
        private Vector2 Indices;
        private MersenneTwister RNG = new MersenneTwister();

        internal void SetIndices (int first, int last) {
            Indices = new Vector2(first, last);
        }

        internal void Tick (out int spawnCount) {
            if (Delay <= 0) {
                spawnCount = (int)(MinCount + (RNG.NextSingle() * (MaxCount - MinCount)));
                Delay = (int)(MinInterval + (RNG.NextSingle() * (MaxInterval - MinInterval)));
            } else {
                spawnCount = 0;
                Delay--;
            }
        }

        internal override Material GetMaterial (ParticleMaterials materials) {
            var result = materials.Spawn;
            // FIXME
            Uniforms = materials.MaterialSet.GetUniformBinding<SpawnerUniforms>(result, "Configuration");
            return result;
        }

        internal override void SetParameters (EffectParameterCollection parameters) {
            Uniforms.Value.Current = new SpawnerUniforms {
                ChunkSize = ParticleSystem.ChunkSize,
                Indices = Indices,
                Position = Position,
                Velocity = Velocity,
                Attributes = Attributes
            };
            Uniforms.Flush();
        }
    }
}
