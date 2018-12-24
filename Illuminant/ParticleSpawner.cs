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
        public const int MaxPositions = 32;

        [NonSerialized]
        private static int NextSeed = 1;

        public float    MinRate, MaxRate;

        public bool     RatePerPosition;

        public Formula  Position = Formula.UnitNormal(), 
            Velocity = Formula.UnitNormal(), 
            Attributes = Formula.One();

        public Matrix   PositionPostMatrix = Matrix.Identity;

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
        private Vector4[] Temp = new Vector4[12];
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

        internal void Tick (double deltaTimeSeconds, out int spawnCount) {
            if (AdditionalPositions.Count >= MaxPositions)
                throw new Exception("Maximum number of positions for a spawner is " + MaxPositions);
            if (!IsActive) {
                RateError = 0;
                spawnCount = 0;
                return;
            }

            var countScaler = RatePerPosition ? AdditionalPositions.Count + 1 : 1;
            var currentRate = ((RNG.NextDouble() * (MaxRate - MinRate)) + MinRate) * countScaler * deltaTimeSeconds;
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

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex) {
            var secs = (float)Squared.Util.Time.Seconds;

            var ro = parameters["RandomnessOffset"];
            if (ro == null)
                return;

            double a = RNG.NextDouble(), b = RNG.NextDouble();

            ro.SetValue(new Vector2(
                (float)(a * 253),
                (float)(b * 127)
            ));

            Temp[0] = Position.RandomOffset;
            Temp[1] = Position.RandomScale;
            Temp[2] = Position.RandomScaleConstant;
            Temp[3] = Velocity.Constant;
            Temp[4] = Velocity.RandomOffset;
            Temp[5] = Velocity.RandomScale;
            Temp[6] = Velocity.RandomScaleConstant;
            Temp[7] = Attributes.Constant;
            Temp[8] = Attributes.RandomOffset;
            Temp[9] = Attributes.RandomScale;
            Temp[10] = Attributes.RandomScaleConstant;

            Temp2[0] = Position.Circular   ? 1 : 0;
            Temp2[1] = Velocity.Circular   ? 1 : 0;
            Temp2[2] = Attributes.Circular ? 1 : 0;

            Temp3[0] = Position.Constant;
            for (var i = 0; (i < AdditionalPositions.Count) && (i < MaxPositions - 1); i++)
                Temp3[i + 1] = AdditionalPositions[i];

            var count = Math.Min(1 + AdditionalPositions.Count, MaxPositions);

            parameters["PositionConstantCount"].SetValue((float)count);
            parameters["Configuration"].SetValue(Temp);
            parameters["RandomCircularity"].SetValue(Temp2);
            parameters["PositionConstants"].SetValue(Temp3);
            parameters["ChunkSizeAndIndices"].SetValue(new Vector4(
                engine.Configuration.ChunkSize, Indices.X, Indices.Y,
                TotalSpawned % count
            ));
            parameters["PositionMatrix"].SetValue(PositionPostMatrix);
        }
    }
}
