using System;
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
using Squared.Util;

namespace Squared.Illuminant.Particles.Transforms {
    public abstract class SpawnerBase : ParticleTransform {
        /// <summary>
        /// Minimum number of particles to spawn per second.
        /// If this value is larger than MaxRate it will be ignored.
        /// </summary>
        public Parameter<float> MinRate;
        /// <summary>
        /// Maximum number of particles to spawn per second.
        /// </summary>
        public Parameter<float> MaxRate;

        /// <summary>
        /// If set, the randomly selected normals for position and velocity will be identical.
        /// If not set, they will have different randomly selected normals.
        /// </summary>
        public bool AlignVelocityAndPosition = false;
        /// <summary>
        /// If set, the Z axis of position and velocity normals will be zero - producing random XY normals.
        /// If not set, random normals will be 3-dimensional.
        /// </summary>
        public bool ZeroZAxis = false;
        /// <summary>
        /// If a new particle's color has an alpha (w) less than this value the particle is discarded.
        /// </summary>
        public float AlphaDiscardThreshold = 1;

        public Formula3 Position = Formula3.UnitNormal();
        public Formula3 Velocity = Formula3.UnitNormal();
        public Formula1 Life = Formula1.One();
        /// <summary>
        /// The category of the particle (controls which row of the texture is used, if applicable)
        /// </summary>
        public Formula1 Category = Formula1.Zero();
        public Formula4 Color = Formula4.One();

        /// <summary>
        /// Applies a matrix transform to particle positions after the position formula has been evaluated.
        /// </summary>
        public Parameter<DynamicMatrix> PositionPostMatrix = DynamicMatrix.Identity;

        /// <summary>
        /// If set, the spawner will only be allowed to produce this many particles in total.
        /// </summary>
        public int? MaximumTotal = null;

        private static int NextSeed = 1;

        protected double  RateError    { get; private set; }
        protected Vector2 Indices      { get; private set; }
        public    int     TotalSpawned { get; private set; }
        [NonSerialized]
        protected readonly MersenneTwister RNG;

        [NonSerialized]
        protected Vector4[] Temp = new Vector4[8];

        [NonSerialized]
        internal readonly UpdateHandler Handler2;

        protected SpawnerBase ()
            : this(null) {
        }

        protected SpawnerBase (int? seed) {
            RNG = new MersenneTwister(seed.GetValueOrDefault(NextSeed++));
            ActiveStateChanged += Spawner_ActiveStateChanged;
            Handler2 = new UpdateHandler(this);
        }

        private void Spawner_ActiveStateChanged () {
            RateError = 0;
        }

        public override void Reset () {
            RateError = 0;
            TotalSpawned = 0;
        }

        internal void SetIndices (int first, int last) {
            Indices = new Vector2(first, last);
        }

        protected virtual int CountScale {
            get {
                return 1;
            }
        }

        protected virtual Vector4 GetChunkSizeAndIndices (ParticleEngine engine) {
            return new Vector4(
                engine.Configuration.ChunkSize, Indices.X, Indices.Y, 0
            );
        }

        public virtual void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            sourceChunk = null;

            if (!IsActive) {
                RateError = 0;
                spawnCount = 0;
                return;
            }

            var countScaler = CountScale;
            float minRate = MinRate.Evaluate((float)now, system.Engine.ResolveSingle), 
                maxRate = MaxRate.Evaluate((float)now, system.Engine.ResolveSingle);
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
            }

            if (MaximumTotal.HasValue) {
                var remaining = MaximumTotal.Value - TotalSpawned;
                if (spawnCount > remaining) {
                    spawnCount = remaining;
                    RateError = 0;
                }
            }
        }

        public virtual void EndTick (int requestedSpawnCount, int actualSpawnCount) {
            RateError += requestedSpawnCount - actualSpawnCount;
            TotalSpawned += actualSpawnCount;
        }

        internal void AddError (int numUnspawned) {
            RateError += numUnspawned;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            var ro = parameters["RandomnessOffset"];
            if (ro == null)
                return;

            if (!BindRandomnessTexture(engine, parameters, true))
                return;

            double a = RNG.NextDouble(), b = RNG.NextDouble();

            ro.SetValue(new Vector2(
                (float)(a * 253),
                (float)(b * 127)
            ));
            parameters["ChunkSizeAndIndices"].SetValue(GetChunkSizeAndIndices(engine));

            var lifeScale = Life.RandomScale.Evaluate(now, engine.ResolveSingle);
            var lifeOffset = Life.Offset.Evaluate(now, engine.ResolveSingle);
            var typeConstant = Category.Constant.Evaluate(now, engine.ResolveSingle);
            var typeScale = Category.RandomScale.Evaluate(now, engine.ResolveSingle);
            var typeOffset = Category.Offset.Evaluate(now, engine.ResolveSingle);
            Temp[0] = new Vector4(Position.RandomScale.Evaluate(now, engine.ResolveVector3), lifeScale);
            Temp[1] = new Vector4(Position.Offset.Evaluate(now, engine.ResolveVector3), lifeOffset);
            Temp[2] = new Vector4(Velocity.Constant.Evaluate(now, engine.ResolveVector3), typeConstant);
            Temp[3] = new Vector4(Velocity.RandomScale.Evaluate(now, engine.ResolveVector3), typeScale);
            Temp[4] = new Vector4(Velocity.Offset.Evaluate(now, engine.ResolveVector3), typeOffset);
            Temp[5] = Color.Constant.Evaluate(now, engine.ResolveVector4);
            Temp[6] = Color.RandomScale.Evaluate(now, engine.ResolveVector4);
            Temp[7] = Color.Offset.Evaluate(now, engine.ResolveVector4);

            var ft = new Vector4(
                (int)Position.Type,
                (int)Velocity.Type,
                0,
                0
            );

            parameters["AlignVelocityAndPosition"].SetValue(
                AlignVelocityAndPosition && Position.Circular && Velocity.Circular
            );
            parameters["ZeroZAxis"].SetValue(ZeroZAxis);
            parameters["Configuration"].SetValue(Temp);
            parameters["FormulaTypes"].SetValue(ft);

            var m = PositionPostMatrix.Evaluate(now, engine.ResolveDynamicMatrix);
            m.Regenerate();
            parameters["PositionMatrix"].SetValue(m.Matrix);

            parameters["AttributeDiscardThreshold"].SetValue(AlphaDiscardThreshold / 255f);
        }
    }

    public sealed class Spawner : SpawnerBase {
        public const int MaxPositions = 32;

        /// <summary>
        /// If set, the spawn position will trace a linear path between each specified
        ///  position from the additional positions list at this rate.
        /// </summary>
        public float? PolygonRate = null;
        /// <summary>
        /// A list of additional positions to spawn particles from.
        /// You can set the W value of a position to -1 for it to inherit the main position's W value.
        /// </summary>
        public readonly List<Vector4> AdditionalPositions = new List<Vector4>();
        /// <summary>
        /// If set, the MinRate and MaxRate parameters apply to each position instead of the spawner as a whole.
        /// </summary>
        public bool RatePerPosition = true;

        [NonSerialized]
        private Vector4[] Temp3 = new Vector4[MaxPositions];

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.Spawn;
        }

        protected override int CountScale {
            get {
                return RatePerPosition ? AdditionalPositions.Count + 1 : 1;
            }
        }

        public override void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            if (AdditionalPositions.Count >= MaxPositions)
                throw new Exception("Maximum number of positions for a spawner is " + MaxPositions);

            base.BeginTick(system, now, deltaTimeSeconds, out spawnCount, out sourceChunk);
        }

        protected override Vector4 GetChunkSizeAndIndices (ParticleEngine engine) {
            var count = Math.Min(1 + AdditionalPositions.Count, MaxPositions);
            var result = base.GetChunkSizeAndIndices(engine);
            var polygonRate = PolygonRate.GetValueOrDefault(0);
            if (polygonRate >= 1) {
                result.W = (TotalSpawned / polygonRate) % (float)count;
            } else {
                result.W = TotalSpawned % count;
            }
            return result;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);

            var life = Life.Constant.Evaluate(now, engine.ResolveSingle);
            var position = Position.Constant.Evaluate(now, engine.ResolveVector3);
            Temp3[0] = new Vector4(position, life);
            for (var i = 0; (i < AdditionalPositions.Count) && (i < MaxPositions - 1); i++) {
                var ap = AdditionalPositions[i];
                if (ap.W <= -0.99)
                    ap.W = life;
                Temp3[i + 1] = ap;
            }

            var count = Math.Min(1 + AdditionalPositions.Count, MaxPositions);
            parameters["PositionConstantCount"].SetValue((float)count);
            parameters["PositionConstants"].SetValue(Temp3);
            parameters["PolygonRate"].SetValue(PolygonRate.GetValueOrDefault(0));
        }

        public override bool IsValid {
            get {
                return true;
            }
        }
    }
}
