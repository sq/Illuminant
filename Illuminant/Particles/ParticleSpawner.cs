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

namespace Squared.Illuminant.Particles.Transforms {
    public class Spawner : ParticleTransform {
        public const int MaxPositions = 32;

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
        /// If set, the MinRate and MaxRate parameters apply to each position instead of the spawner as a whole.
        /// </summary>
        public bool RatePerPosition = true;
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

        public Formula  Position = Formula.UnitNormal(), 
            Velocity = Formula.UnitNormal(), 
            Attributes = Formula.One();

        /// <summary>
        /// Applies a matrix transform to particle positions after the position formula has been evaluated.
        /// </summary>
        public Parameter<DynamicMatrix> PositionPostMatrix = DynamicMatrix.Identity;

        /// <summary>
        /// A list of additional positions to spawn particles from.
        /// You can set the W value of a position to -1 for it to inherit the main position's W value.
        /// </summary>
        public readonly List<Vector4> AdditionalPositions = new List<Vector4>();

        [NonSerialized]
        private static int NextSeed = 1;

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

        internal virtual void BeginTick (ParticleSystem system, float now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            sourceChunk = null;

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
            }
        }

        internal virtual void EndTick (int requestedSpawnCount, int actualSpawnCount) {
            RateError += requestedSpawnCount - actualSpawnCount;
            TotalSpawned += actualSpawnCount;
        }

        internal void AddError (int numUnspawned) {
            RateError += numUnspawned;
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

            parameters["AlignVelocityAndPosition"].SetValue(
                AlignVelocityAndPosition && Position.Circular && Velocity.Circular
            );
            parameters["ZeroZAxis"].SetValue(ZeroZAxis);
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

        public override bool IsValid {
            get {
                return true;
            }
        }
    }

    public sealed class FeedbackSpawner : Spawner {
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

        internal override void BeginTick (ParticleSystem system, float now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
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
            var windowedAvailable = Math.Min(availableForFeedback, windowSize);

            var skipAmount = Math.Max(0, availableForFeedback - windowedAvailable);
            sourceChunk.SkipFeedbackInput(skipAmount);

            var availableLessMargin = Math.Max(0, windowedAvailable - SlidingWindowMargin);

            spawnCount = Math.Min(spawnCount, availableLessMargin);
            CurrentFeedbackSource = sourceChunk;
            CurrentFeedbackSourceIndex = sourceChunk.FeedbackSourceIndex;
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.SpawnFeedback;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);

            parameters["AlignPositionConstant"].SetValue(AlignPositionConstant);
            parameters["MultiplyAttributeConstant"].SetValue(MultiplyAttributeConstant);
            parameters["FeedbackSourceIndex"].SetValue(CurrentFeedbackSourceIndex);
        }

        public override bool IsValid {
            get {
                return true;
            }
        }
    }
}
