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
        /// If a new particle's attribute has an alpha (w) less than this value the particle is discarded.
        /// </summary>
        public float AttributeDiscardThreshold = 1.0f / 256;

        public Formula3 Position = Formula3.UnitNormal(),
            Velocity = Formula3.UnitNormal();
        public Formula1 Life = Formula1.One();
        public Formula4 Attributes = Formula4.One();

        /// <summary>
        /// Applies a matrix transform to particle positions after the position formula has been evaluated.
        /// </summary>
        public Parameter<DynamicMatrix> PositionPostMatrix = DynamicMatrix.Identity;

        private static int NextSeed = 1;

        protected double   RateError { get; private set; }
        protected Vector2  Indices { get; private set; }
        protected int      TotalSpawned { get; private set; }
        [NonSerialized]
        protected readonly MersenneTwister RNG;

        [NonSerialized]
        protected Vector4[] Temp = new Vector4[8];

        protected SpawnerBase ()
            : this(null) {
        }

        protected SpawnerBase (int? seed) {
            RNG = new MersenneTwister(seed.GetValueOrDefault(NextSeed++));
            ActiveStateChanged += Spawner_ActiveStateChanged;
        }

        private void Spawner_ActiveStateChanged () {
            RateError = 0;
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

        public virtual void BeginTick (ParticleSystem system, float now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            sourceChunk = null;

            if (!IsActive) {
                RateError = 0;
                spawnCount = 0;
                return;
            }

            var countScaler = CountScale;
            float minRate = MinRate.Evaluate(now, system.Engine.ResolveSingle), maxRate = MaxRate.Evaluate(now, system.Engine.ResolveSingle);
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
        }

        public virtual void EndTick (int requestedSpawnCount, int actualSpawnCount) {
            RateError += requestedSpawnCount - actualSpawnCount;
            TotalSpawned += actualSpawnCount;
        }

        internal void AddError (int numUnspawned) {
            RateError += numUnspawned;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            var secs = (float)Squared.Util.Time.Seconds;

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
            Temp[0] = new Vector4(Position.RandomScale.Evaluate(now, engine.ResolveVector3), lifeScale);
            Temp[1] = new Vector4(Position.Offset.Evaluate(now, engine.ResolveVector3), lifeOffset);
            Temp[2] = new Vector4(Velocity.Constant.Evaluate(now, engine.ResolveVector3), 0);
            Temp[3] = new Vector4(Velocity.RandomScale.Evaluate(now, engine.ResolveVector3), 0);
            Temp[4] = new Vector4(Velocity.Offset.Evaluate(now, engine.ResolveVector3), 0);
            Temp[5] = Attributes.Constant.Evaluate(now, engine.ResolveVector4);
            Temp[6] = Attributes.RandomScale.Evaluate(now, engine.ResolveVector4);
            Temp[7] = Attributes.Offset.Evaluate(now, engine.ResolveVector4);

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

            parameters["AttributeDiscardThreshold"].SetValue(AttributeDiscardThreshold);
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

        public override void BeginTick (ParticleSystem system, float now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
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

    public sealed class PatternSpawner : SpawnerBase {
        private int RowsSpawned = 0;

        /// <summary>
        /// The pattern spawner generates particles corresponding to the pixels of this texture.
        /// </summary>
        public NullableLazyResource<Texture2D> Texture = new NullableLazyResource<Texture2D>();

        /// <summary>
        /// Adjusts the number of particles created per pixel.
        /// </summary>
        public float Resolution = 1;

        /// <summary>
        /// If false, particles for the pattern can be spawned incrementally across frames. If true, only an entire set of particles will be spawned.
        /// </summary>
        public bool WholeSpawn = false;

        /// <summary>
        /// Multiplies the attribute Constant of new particles by the color of the source pixel instead of adding to it.
        /// </summary>
        public bool MultiplyAttributeConstant = true;

        [NonSerialized]
        private Vector4[] Temp3 = new Vector4[1];

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.SpawnPattern;
        }

        private float EffectiveResolution {
            get {
                return (float)Arithmetic.Clamp(Math.Round(Resolution, 2), 0.2, 1.0);
            }
        }

        private int ParticlesPerRow {
            get {
                return (int)Math.Ceiling(Texture.Instance.Width * EffectiveResolution);
            }
        }

        private int RowsPerInstance {
            get {
                return (int)Math.Ceiling(Texture.Instance.Height * EffectiveResolution);
            }
        }

        private int ParticlesPerInstance {
            get {
                return ParticlesPerRow * RowsPerInstance;
            }
        }

        public override void BeginTick (ParticleSystem system, float now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            if (Texture != null)
                Texture.EnsureInitialized(system.Engine.Configuration.TextureLoader);

            if ((Texture == null) || (Texture.Instance == null)) {
                spawnCount = 0;
                sourceChunk = null;
                return;
            }

            base.BeginTick(system, now, deltaTimeSeconds, out spawnCount, out sourceChunk);

            var minCount = WholeSpawn ? ParticlesPerInstance : ParticlesPerRow;
            var requestedSpawnCount = spawnCount;
            if (spawnCount < minCount) {
                AddError(spawnCount);
                spawnCount = 0;
            } else {
                spawnCount = (spawnCount / minCount) * minCount;
                AddError(requestedSpawnCount - spawnCount);
            }
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);

            var life = Life.Constant.Evaluate(now, engine.ResolveSingle);
            var position = Position.Constant.Evaluate(now, engine.ResolveVector3);
            Temp3[0] = new Vector4(position, life);
            parameters["PositionConstantCount"].SetValue((float)1);
            parameters["PositionConstants"].SetValue(Temp3);
            parameters["MultiplyAttributeConstant"].SetValue(MultiplyAttributeConstant);
            parameters["PatternTexture"].SetValue(Texture.Instance);
            parameters["PatternSizeRowSizeAndResolution"].SetValue(new Vector4(
                Texture.Instance.Width, Texture.Instance.Height,
                ParticlesPerRow, EffectiveResolution
            ));

            if (WholeSpawn) {
                parameters["InitialPatternXY"].SetValue(Vector2.Zero);
            } else {
                var currentRow = ((RowsSpawned++) % RowsPerInstance);
                var xyInCurrentInstance = new Vector2(0, currentRow / EffectiveResolution);
                parameters["InitialPatternXY"].SetValue(xyInCurrentInstance);
            }
        }

        public override bool IsValid {
            get {
                return (Texture != null) && ((Texture.Name != null) || (Texture.Instance != null));
            }
        }
    }

    public sealed class FeedbackSpawner : SpawnerBase {
        /// <summary>
        /// The feedback spawner uses the particles contained by the source system as input
        /// </summary>
        public ParticleSystemReference SourceSystem;

        /// <summary>
        /// Adds the position of source particles to the position Constant of new particles
        /// </summary>
        public bool AlignPositionConstant = true;

        /// <summary>
        /// Multiplies the attribute Constant of new particles by the attribute of source particles
        /// </summary>
        public bool MultiplyAttributeConstant = false;

        /// <summary>
        /// The new particles inherit the source particles' velocities as a velocity constant, multiplied
        ///  by this factor
        /// </summary>
        public float SourceVelocityFactor = 0.0f;

        /// <summary>
        /// Only considers the N most recently spawned particles from the source system for feedback.
        /// Use this for cases where the source system spawns at a much higher rate than your spawner,
        ///  to avoid performing feedback spawning against particles that are already dead.
        /// Note that this will have bad interactions with other feedback spawners consuming particles
        ///  from the same source.
        /// </summary>
        public int? SlidingWindowSize = null;

        /// <summary>
        /// Waits until N additional particles are ready to consume for feedback. This provides a
        ///  brute force way to wait until particles have aged before using them for feedback.
        /// </summary>
        public int SlidingWindowMargin = 0;
        
        private ParticleSystem.Chunk CurrentFeedbackSource;
        private int CurrentFeedbackSourceIndex;

        private Vector4[] Temp3 = new Vector4[1];

        public override void BeginTick (ParticleSystem system, float now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            spawnCount = 0;
            sourceChunk = null;
            CurrentFeedbackSource = null;

            if (!SourceSystem.TryInitialize(system.Engine.Configuration.SystemResolver))
                return;

            // FIXME: Support using the same system as a feedback input?
            if (SourceSystem.Instance == system)
                return;

            base.BeginTick(system, now, deltaTimeSeconds, out spawnCount, out sourceChunk);

            sourceChunk = SourceSystem.Instance.PickSourceForFeedback(spawnCount);
            if (sourceChunk == null) {
                spawnCount = 0;
                return;
            }

            var windowSize = SlidingWindowSize.GetValueOrDefault(999999);
            var availableForFeedback = sourceChunk.AvailableForFeedback;
            if (sourceChunk.NoLongerASpawnTarget) {
                // HACK: While we can't use two chunks as input at once, we want the window and margin
                //  math to consider the number of particles available in both chunks.
                // Without doing this, spawning will stop at a chunk transition if a margin is set at all.
                var currentWriteChunk = SourceSystem.Instance.GetCurrentSpawnTarget(false);
                if (currentWriteChunk != null)
                    availableForFeedback += currentWriteChunk.AvailableForFeedback;
            }
            var windowedAvailable = Math.Min(availableForFeedback, windowSize);

            var skipAmount = Math.Max(0, availableForFeedback - windowedAvailable);
            sourceChunk.SkipFeedbackInput(skipAmount);

            var availableLessMargin = Math.Max(0, windowedAvailable - SlidingWindowMargin);

            spawnCount = Math.Min(spawnCount, availableLessMargin);
            // Now actually clamp it to the amount available after applying the window math that considers
            //  chunk transitions
            spawnCount = Math.Min(spawnCount, sourceChunk.AvailableForFeedback);
            CurrentFeedbackSource = sourceChunk;
            CurrentFeedbackSourceIndex = sourceChunk.FeedbackSourceIndex;
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.SpawnFeedback;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);

            var life = Life.Constant.Evaluate(now, engine.ResolveSingle);
            var position = Position.Constant.Evaluate(now, engine.ResolveVector3);
            Temp3[0] = new Vector4(position, life);
            parameters["PositionConstantCount"].SetValue((float)1);
            parameters["PositionConstants"].SetValue(Temp3);
            parameters["AlignPositionConstant"].SetValue(AlignPositionConstant);
            parameters["MultiplyAttributeConstant"].SetValue(MultiplyAttributeConstant);
            parameters["FeedbackSourceIndex"].SetValue(CurrentFeedbackSourceIndex);
            parameters["SourceVelocityFactor"].SetValue(SourceVelocityFactor);
        }

        public override bool IsValid {
            get {
                return true;
            }
        }
    }
}
