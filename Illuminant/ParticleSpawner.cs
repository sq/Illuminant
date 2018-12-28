﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Configuration;
using Squared.Illuminant.Uniforms;
using Squared.Illuminant.Util;
using Squared.Render;

namespace Squared.Illuminant.Particles.Transforms {
    public class Spawner : ParticleTransform {
        public const int MaxPositions = 32;

        [NonSerialized]
        private static int NextSeed = 1;

        public Configuration.Parameter<float> MinRate, MaxRate;

        public bool     RatePerPosition;

        public Formula  Position = Formula.UnitNormal(), 
            Velocity = Formula.UnitNormal(), 
            Attributes = Formula.One();

        public Configuration.Parameter<DynamicMatrix> PositionPostMatrix = DynamicMatrix.Identity;

        /// <summary>
        /// You can set the W value of a position to -1 for it to inherit the main position's W value
        /// </summary>
        public readonly List<Vector4> AdditionalPositions = new List<Vector4>();

        [NonSerialized]
        private double  RateError;
        [NonSerialized]
        private Vector2 Indices;
        [NonSerialized]
        private MersenneTwister RNG;
        [NonSerialized]
        private int     TotalSpawned;

        [NonSerialized]
        private Vector4[] Temp = new Vector4[8];
        [NonSerialized]
        private float[] Temp2 = new float[3];
        [NonSerialized]
        private Vector4[] Temp3 = new Vector4[MaxPositions];

        public Spawner ()
            : this(null) {
        }

        public Spawner (int? seed) {
            RNG = new MersenneTwister(seed.GetValueOrDefault(NextSeed++));
            ActiveStateChanged += Spawner_ActiveStateChanged;
        }

        private void Spawner_ActiveStateChanged () {
            RateError = 0;
        }

        internal void SetIndices (int first, int last) {
            Indices = new Vector2(first, last);
        }

        internal void Tick (float now, double deltaTimeSeconds, out int spawnCount) {
            if (AdditionalPositions.Count >= MaxPositions)
                throw new Exception("Maximum number of positions for a spawner is " + MaxPositions);
            if (!IsActive) {
                RateError = 0;
                spawnCount = 0;
                return;
            }

            var countScaler = RatePerPosition ? AdditionalPositions.Count + 1 : 1;
            float minRate = MinRate.Evaluate(now), maxRate = MaxRate.Evaluate(now);
            if (minRate > maxRate)
                minRate = maxRate;
            var currentRate = ((RNG.NextDouble() * (maxRate - minRate)) + minRate) * countScaler * deltaTimeSeconds;
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

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.Spawn;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            var secs = (float)Squared.Util.Time.Seconds;

            var ro = parameters["RandomnessOffset"];
            if (ro == null)
                return;

            double a = RNG.NextDouble(), b = RNG.NextDouble();

            ro.SetValue(new Vector2(
                (float)(a * 253),
                (float)(b * 127)
            ));

            Temp[0] = Position.RandomScale.Evaluate(now);
            Temp[1] = Position.Offset.Evaluate(now);
            Temp[2] = Velocity.Constant.Evaluate(now);
            Temp[3] = Velocity.RandomScale.Evaluate(now);
            Temp[4] = Velocity.Offset.Evaluate(now);
            Temp[5] = Attributes.Constant.Evaluate(now);
            Temp[6] = Attributes.RandomScale.Evaluate(now);
            Temp[7] = Attributes.Offset.Evaluate(now);

            Temp2[0] = Position.Circular   ? 1 : 0;
            Temp2[1] = Velocity.Circular   ? 1 : 0;
            Temp2[2] = Attributes.Circular ? 1 : 0;

            var position = Position.Constant.Evaluate(now);
            Temp3[0] = position;
            for (var i = 0; (i < AdditionalPositions.Count) && (i < MaxPositions - 1); i++) {
                var ap = AdditionalPositions[i];
                if (ap.W <= -0.99)
                    ap.W = position.W;
                Temp3[i + 1] = ap;
            }

            var count = Math.Min(1 + AdditionalPositions.Count, MaxPositions);

            parameters["PositionConstantCount"].SetValue((float)count);
            parameters["Configuration"].SetValue(Temp);
            parameters["RandomCircularity"].SetValue(Temp2);
            parameters["PositionConstants"].SetValue(Temp3);
            parameters["ChunkSizeAndIndices"].SetValue(new Vector4(
                engine.Configuration.ChunkSize, Indices.X, Indices.Y,
                TotalSpawned % count
            ));
            var m = PositionPostMatrix.Evaluate(now);
            m.Regenerate();
            parameters["PositionMatrix"].SetValue(m.Matrix);
        }
    }
}
