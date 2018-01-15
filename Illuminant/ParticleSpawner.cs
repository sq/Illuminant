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

        private int     Delay;
        private Vector2 Indices;
        private MersenneTwister RNG = new MersenneTwister();
        private int     TotalSpawned;

        private Vector4[] Temp = new Vector4[9];
        private float[] Temp2 = new float[3];

        internal void SetIndices (int first, int last) {
            Indices = new Vector2(first, last);
        }

        internal void Tick (out int spawnCount) {
            if (Delay <= 0) {
                spawnCount = (int)(MinCount + (RNG.NextSingle() * (MaxCount - MinCount)));
                Delay = (int)(MinInterval + (RNG.NextSingle() * (MaxInterval - MinInterval)));
                TotalSpawned += spawnCount;
            } else {
                spawnCount = 0;
                Delay--;
            }
        }

        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.Spawn;
        }

        internal override void SetParameters (EffectParameterCollection parameters) {
            var secs = (float)Squared.Util.Time.Seconds;

            var ro = parameters["RandomnessOffset"];
            if (ro == null)
                return;

            ro.SetValue(new Vector2(
                (TotalSpawned % ParticleEngine.RandomnessTextureWidth) + (secs * 32), 
                (TotalSpawned / ParticleEngine.RandomnessTextureWidth) * 2 + secs
            ));

            Temp[0] = Position.Constant;
            Temp[1] = Position.RandomOffset;
            Temp[2] = Position.RandomScale;
            Temp[3] = Velocity.Constant;
            Temp[4] = Velocity.RandomOffset;
            Temp[5] = Velocity.RandomScale;
            Temp[6] = Attributes.Constant;
            Temp[7] = Attributes.RandomOffset;
            Temp[8] = Attributes.RandomScale;

            Temp2[0] = Position.RandomCircularity;
            Temp2[1] = Velocity.RandomCircularity;
            Temp2[2] = Attributes.RandomCircularity;

            parameters["Configuration"].SetValue(Temp);
            parameters["RandomCircularity"].SetValue(Temp2);
            parameters["ChunkSizeAndIndices"].SetValue(new Vector4(
                ParticleSystem.ChunkSize.X, ParticleSystem.ChunkSize.Y,
                Indices.X, Indices.Y
            ));
        }
    }
}
