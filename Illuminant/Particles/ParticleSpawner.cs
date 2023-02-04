using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
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
        /// If set, the spawner will only be allowed to produce this many particles in total.
        /// </summary>
        public int? MaximumTotal = null;

        /// <summary>
        /// The position of the particle.
        /// </summary>
        public Formula3 Position = Formula3.UnitNormal();
        /// <summary>
        /// Applies a matrix transform to particle positions after the position formula has been evaluated.
        /// </summary>
        public Parameter<DynamicMatrix> PositionPostMatrix = DynamicMatrix.Identity;
        /// <summary>
        /// Applies a matrix transform to particle velocities after the velocity formula has been evaluated.
        /// </summary>
        public Parameter<DynamicMatrix> VelocityPostMatrix = DynamicMatrix.Identity;

        /// <summary>
        /// If set, the randomly selected normals for position and velocity will be identical.
        /// If not set, they will have different randomly selected normals.
        /// </summary>
        public bool AlignVelocityAndPosition = false;
        /// <summary>
        /// Allows selecting two out of three axes to use for selected normals, producing a ring instead of a sphere.
        /// </summary>
        public Vector3 AxisMask = Vector3.One;

        protected bool ZeroZAxis {
            set {
                if (value)
                    AxisMask = new Vector3(1, 1, 0);
            }
        }

        /// <summary>
        /// The velocity of the particle.
        /// </summary>
        public Formula3 Velocity = Formula3.UnitNormal();
        /// <summary>
        /// The life value of the particle.
        /// </summary>
        public Formula1 Life = Formula1.One();
        /// <summary>
        /// The category of the particle (controls which row of the texture is used, if applicable)
        /// </summary>
        public Formula1 Category = Formula1.Zero();
        /// <summary>
        /// The constant color of the particle (multiplied by other color settings and ramps).
        /// </summary>
        public Formula4 Color = Formula4.One();
        /// <summary>
        /// If a new particle's color has an alpha (w) less than this value the particle is discarded.
        /// </summary>
        public float AlphaDiscardThreshold = 1;

        private static int NextSeed = 1;

        protected double  RateError    { get;         set; }
        protected Vector2 Indices      { get; private set; }
        public    int     TotalSpawned { get; private set; }
        [NonSerialized]
        protected CoreCLR.Xoshiro RNG;

        [NonSerialized]
        protected Vector4[] Temp = new Vector4[9];

        [NonSerialized]
        internal readonly UpdateHandler Handler2;

        public virtual bool PartialSpawnAllowed {
            get {
                return true;
            }
        }

        protected SpawnerBase ()
            : this(null) {
            IsAnalyzer = false;
        }

        protected SpawnerBase (CoreCLR.Xoshiro? rng) {
            RNG = rng ?? new CoreCLR.Xoshiro(null);
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

        public virtual float EstimateMaximumLifeForNewParticle (float now, NamedConstantResolver<float> nameResolver) {
            var c = Life.Constant.Evaluate(now, nameResolver);
            var o = Life.Offset.Evaluate(now, nameResolver);
            var s = Life.RandomScale.Evaluate(now, nameResolver);

            var a = c + (o * s);
            var b = c - (o * s);
            return Math.Max(a, b);
        }

        protected virtual Vector4 GetChunkSizeAndIndices (ParticleEngine engine) {
            return new Vector4(
                engine.Configuration.ChunkSize, Indices.X, Indices.Y, 0
            );
        }

        protected virtual double AdjustCurrentRate (double rate) {
            return rate;
        }

        public virtual void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            sourceChunk = null;

            if (!IsActive || !IsActive2) {
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
            RateError = 0;
            currentRate = AdjustCurrentRate(currentRate);
            if (currentRate < 1) {
                RateError = Math.Max(currentRate, 0);
                spawnCount = 0;
            } else {
                spawnCount = (int)currentRate;
                RateError = currentRate - spawnCount;
            }

            if (MaximumTotal.HasValue) {
                var scaledTotal = MaximumTotal.Value * CountScale;
                var remaining = scaledTotal - TotalSpawned;
                if (spawnCount > remaining) {
                    spawnCount = remaining;
                    RateError = 0;
                }
            }

            if (spawnCount > 0)
                ;
        }

        public virtual void EndTick (int requestedSpawnCount, int actualSpawnCount) {
            RateError += requestedSpawnCount - actualSpawnCount;
            TotalSpawned += actualSpawnCount;
        }

        internal void AddError (int numUnspawned) {
            RateError += numUnspawned;
        }

        protected override void SetParameters (ParticleEngine engine, MaterialEffectParameters parameters, float now, int frameIndex) {
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

            InitConfiguration(engine, now, ref Temp, ref ft);

            parameters["AlignVelocityAndPosition"].SetValue(
                (AlignVelocityAndPosition && Position.Circular && Velocity.Circular) ? 1f : 0f
            );
            parameters["AxisMask"].SetValue(AxisMask);
            parameters["Configuration"].SetValue(Temp);
            parameters["FormulaTypes"].SetValue(ft);

            var m = PositionPostMatrix.Evaluate(now, engine.ResolveDynamicMatrix);
            m.Regenerate();
            parameters["PositionMatrix"].SetValue(m.Matrix);

            m = VelocityPostMatrix.Evaluate(now, engine.ResolveDynamicMatrix);
            m.Regenerate();
            parameters["VelocityMatrix"].SetValue(m.Matrix);

            parameters["AttributeDiscardThreshold"].SetValue(AlphaDiscardThreshold / 255f);
        }

        protected virtual void InitConfiguration (ParticleEngine engine, float now, ref Vector4[] configuration, ref Vector4 formulaTypes) {
        }
    }

    public sealed class Spawner : SpawnerBase {
        public const int MaxInlinePositions = 4;

        /// <summary>
        /// A list of additional positions to spawn particles from.
        /// </summary>
        public readonly List<Vector3> AdditionalPositions = new List<Vector3>();
        /// <summary>
        /// If set, the spawn position will trace a linear path between each specified position
        ///  at this rate.
        /// </summary>
        public float? PolygonRate = null;
        /// <summary>
        /// If set, polygonal spawn positions will form a closed shape with the last edge being between
        ///  the last additional position and the first spawn position.
        /// </summary>
        public bool PolygonLoop = true;
        /// <summary>
        /// The velocity of the particle towards the next point in the spawn polygon (if any)
        /// </summary>
        public Formula1 VelocityAlongPolygon = Formula1.Zero();
        /// <summary>
        /// If set, the MinRate and MaxRate parameters apply to each position instead of the spawner as a whole.
        /// </summary>
        public bool RatePerPosition = true;

        [NonSerialized]
        private Vector4[] Temp3 = new Vector4[MaxInlinePositions];
        [NonSerialized]
        private Vector4[] Temp4;
        [NonSerialized]
        private Texture2D PositionBuffer;

        protected override Material GetMaterial (ParticleMaterials materials) {
            return (AdditionalPositions.Count >= MaxInlinePositions)
                ? materials.SpawnFromPositionTexture
                : materials.Spawn;
        }

        protected override int CountScale {
            get {
                return Math.Max(RatePerPosition ? AdditionalPositions.Count + (PolygonLoop ? 1 : 0) : 1, 1);
            }
        }

        private void EnsurePositionBufferExists (ParticleEngine engine, int count) {
            if ((PositionBuffer != null) && (PositionBuffer.Width < count)) {
                engine.Coordinator.DisposeResource(PositionBuffer);
                PositionBuffer = null;
                Temp4 = null;
            }
            if (PositionBuffer == null) {
                var bufSize = (count + 127) / 128 * 128;
                Temp4 = new Vector4[bufSize];
                lock (engine.Coordinator.CreateResourceLock)
                    PositionBuffer = new Texture2D(engine.Coordinator.Device, bufSize, 1, false, SurfaceFormat.Vector4);
            }
        }

        public override void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            base.BeginTick(system, now, deltaTimeSeconds, out spawnCount, out sourceChunk);

            var life = Life.Constant.Evaluate((float)now, system.Engine.ResolveSingle);
            var position = Position.Constant.Evaluate((float)now, system.Engine.ResolveVector3);

            var count = AdditionalPositions.Count + 1;
            if (count > MaxInlinePositions) {
                EnsurePositionBufferExists(system.Engine, count);

                var dirty = false;

                var v = new Vector4(position, life);
                if (Temp4[0] != v) {
                    dirty = true;
                    Temp4[0] = v;
                }

                for (var i = 0; i < AdditionalPositions.Count; i++) {
                    var ap = AdditionalPositions[i];
                    v = new Vector4(ap, life);
                    if (Temp4[i + 1].FastEquals(in v))
                        continue;

                    Temp4[i + 1] = v;
                    dirty = true;
                }

                if (dirty)
                lock (system.Engine.Coordinator.UseResourceLock)
                    PositionBuffer.SetData(Temp4);
            } else {
                Temp3[0] = new Vector4(position, life);
                for (var i = 0; (i < AdditionalPositions.Count) && (i < MaxInlinePositions - 1); i++) {
                    var ap = AdditionalPositions[i];
                    Temp3[i + 1] = new Vector4(ap, life);
                }
            }
        }

        protected override Vector4 GetChunkSizeAndIndices (ParticleEngine engine) {
            var count = 1 + AdditionalPositions.Count;
            var result = base.GetChunkSizeAndIndices(engine);
            var polygonRate = PolygonRate.GetValueOrDefault(0);
            if (polygonRate >= 1) {
                if (!PolygonLoop)
                    count -= 1;
                result.W = (TotalSpawned / polygonRate) % (float)count;
            } else {
                result.W = TotalSpawned % count;
            }
            return result;
        }

        protected override void SetParameters (ParticleEngine engine, MaterialEffectParameters parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);

            var count = 1 + AdditionalPositions.Count;
            if (count > MaxInlinePositions) {
                EnsurePositionBufferExists(engine, count);
                parameters["PositionConstantTexel"].SetValue(new Vector2(1.0f / PositionBuffer.Width, 1.0f / PositionBuffer.Height));
                parameters["PositionConstantTexture"].SetValue(PositionBuffer);
            } else {
                parameters["InlinePositionConstants"].SetValue(Temp3);
                parameters["PositionConstantTexture"]?.SetValue((Texture2D)null);
            }
            parameters["PositionConstantCount"].SetValue((float)count);
            parameters["PolygonRate"].SetValue(PolygonRate.GetValueOrDefault(0));
            parameters["PolygonLoop"].SetValue(PolygonLoop ? 1f : 0f);
        }

        protected override void InitConfiguration (ParticleEngine engine, float now, ref Vector4[] configuration, ref Vector4 formulaTypes) {
            base.InitConfiguration(engine, now, ref configuration, ref formulaTypes);

            configuration[8] = new Vector4(
                VelocityAlongPolygon.Constant.Evaluate(now, engine.ResolveSingle),
                VelocityAlongPolygon.RandomScale.Evaluate(now, engine.ResolveSingle),
                VelocityAlongPolygon.Offset.Evaluate(now, engine.ResolveSingle),
                0
            );
            formulaTypes.W = 0;
        }

        public override void Dispose () {
            base.Dispose();

            if (PositionBuffer != null) {
                PositionBuffer.Dispose();
                PositionBuffer = null;
            }
        }

        public override bool IsValid {
            get {
                return true;
            }
        }
    }
}
