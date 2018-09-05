using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Uniforms;
using Squared.Illuminant.Util;
using Squared.Render;

namespace Squared.Illuminant.Particles.Transforms {
    public class Spawner : ParticleTransform {
        private static int NextSeed = 1;

        public float    MinRate, MaxRate;
        private double  RateError;

        public Formula  Position, Velocity, Attributes;

        private Vector2 Indices;
        private MersenneTwister RNG;
        private int     TotalSpawned;

        private Vector4[] Temp = new Vector4[12];
        private float[] Temp2 = new float[3];

        public Spawner (int? seed = null) {
            RNG = new MersenneTwister(seed.GetValueOrDefault(NextSeed++));
        }

        internal void SetIndices (int first, int last) {
            Indices = new Vector2(first, last);
        }

        internal void Tick (double deltaTimeSeconds, out int spawnCount) {
            var currentRate = ((RNG.NextDouble() * (MaxRate - MinRate)) + MinRate) * deltaTimeSeconds;
            currentRate += RateError;
            if (currentRate < 1) {
                RateError = Math.Max(currentRate, 0);
                spawnCount = 0;
            } else {
                spawnCount = (int)currentRate;
                RateError = currentRate - spawnCount;
                TotalSpawned += spawnCount;
            }
        }

        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.Spawn;
        }

        internal override void SetParameters (EffectParameterCollection parameters, int frameIndex) {
            var secs = (float)Squared.Util.Time.Seconds;

            var ro = parameters["RandomnessOffset"];
            if (ro == null)
                return;

            double a = RNG.NextDouble(), b = RNG.NextDouble();

            ro.SetValue(new Vector2(
                (float)(a * 253),
                (float)(b * 127)
            ));

            Temp[0] = Position.Constant;
            Temp[1] = Position.RandomOffset;
            Temp[2] = Position.RandomScale;
            Temp[3] = Position.RandomScaleConstant;
            Temp[4] = Velocity.Constant;
            Temp[5] = Velocity.RandomOffset;
            Temp[6] = Velocity.RandomScale;
            Temp[7] = Velocity.RandomScaleConstant;
            Temp[8] = Attributes.Constant;
            Temp[9] = Attributes.RandomOffset;
            Temp[10] = Attributes.RandomScale;
            Temp[11] = Attributes.RandomScaleConstant;

            Temp2[0] = Position.RandomCircularity;
            Temp2[1] = Velocity.RandomCircularity;
            Temp2[2] = Attributes.RandomCircularity;

            parameters["Configuration"].SetValue(Temp);
            parameters["RandomCircularity"].SetValue(Temp2);
            parameters["ChunkSizeAndIndices"].SetValue(new Vector4(
                Engine.Configuration.ChunkSize, Engine.Configuration.ChunkSize,
                Indices.X, Indices.Y
            ));
        }
    }
}
