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
    public sealed class PatternSpawner : SpawnerBase {
        [NonSerialized]
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
        /// Multiplies the color Constant of new particles by the color of the source pixel instead of adding to it.
        /// </summary>
        public bool MultiplyColorConstant = true;

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

        public override void Reset () {
            RowsSpawned = 0;
        }

        public override void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            if (Texture != null)
                Texture.EnsureInitialized(system.Engine.Configuration.TextureLoader);

            if ((Texture == null) || !Texture.IsInitialized) {
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
            parameters["MultiplyAttributeConstant"].SetValue(MultiplyColorConstant);
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
                return (Texture != null) && ((Texture.Name != null) || Texture.IsInitialized);
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
        /// Multiplies the color Constant of new particles by the attribute of source particles
        /// </summary>
        public bool MultiplyColorConstant = false;

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
        
        [NonSerialized]
        private ParticleSystem.Chunk CurrentFeedbackSource;
        [NonSerialized]
        private int CurrentFeedbackSourceIndex;

        [NonSerialized]
        private Vector4[] Temp3 = new Vector4[1];

        public override void Reset () {
            CurrentFeedbackSourceIndex = 0;
            CurrentFeedbackSource = null;
        }

        public override void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
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
            parameters["MultiplyAttributeConstant"].SetValue(MultiplyColorConstant);
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
