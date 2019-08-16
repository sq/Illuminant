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

        private int _Divisor = 1;

        /// <summary>
        /// Adjusts the number of particles created per pixel. A higher divisor means fewer particles.
        /// </summary>
        public int Divisor {
            get {
                return _Divisor;
            }
            set {
                _Divisor = Arithmetic.Clamp(value, 1, 8);
            }
        }

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

        private static int NextPowerOfTwo (int value) {
            return (int)Math.Pow(2, Math.Ceiling(Math.Log(value, 2)));
        }

        private int NPOTWidth {
            get {
                return NextPowerOfTwo(Texture.Instance.Width);
            }
        }

        private int NPOTHeight {
            get {
                return NextPowerOfTwo(Texture.Instance.Height);
            }
        }

        private int ParticlesPerRow {
            get {
                return NextPowerOfTwo(NPOTWidth / Divisor);
            }
        }

        private int RowsPerInstance {
            get {
                return NextPowerOfTwo(NPOTHeight / Divisor);
            }
        }

        private int ParticlesPerInstance {
            get {
                return ParticlesPerRow * RowsPerInstance;
            }
        }

        protected override int CountScale {
            get {
                if (WholeSpawn)
                    return ParticlesPerInstance;
                else
                    return ParticlesPerRow;
            }
        }

        public override void Reset () {
            base.Reset();
            RowsSpawned = 0;
            PartialSpawnAllowed = false;
        }

        public override void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            PartialSpawnAllowed = false;

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

            var tex = Texture.Instance;
            if (tex == null)
                return;

            var stepValue = Divisor;
            var currentRow =
                WholeSpawn
                    ? 0
                    : (RowsSpawned++) % RowsPerInstance;

            var stepWidthAndSizeScale = new Vector4(
                Divisor, ParticlesPerRow, 
                Divisor / (float)NPOTWidth, Divisor / (float)NPOTHeight
            );

            var yOffsetsAndCoordScale = new Vector4(
                currentRow, (stepValue * currentRow / Divisor) / NPOTHeight,
                Divisor * ((float)tex.Width / NPOTWidth),
                Divisor * ((float)tex.Height / NPOTHeight)
            );

            var texelOffsetAndMipBias = new Vector4(
                -0.5f / tex.Width, -0.5f / tex.Height, 0,
                (float)Math.Log(Divisor, 2) - 0.5f
            );

            var centeringOffset = new Vector2(-tex.Width / 2, -tex.Height / 2);

            parameters["CenteringOffset"].SetValue(centeringOffset);
            parameters["StepWidthAndSizeScale"].SetValue(stepWidthAndSizeScale);
            parameters["YOffsetsAndCoordScale"].SetValue(yOffsetsAndCoordScale);
            parameters["TexelOffsetAndMipBias"].SetValue(texelOffsetAndMipBias);

            var life = Life.Constant.Evaluate(now, engine.ResolveSingle);
            var position = Position.Constant.Evaluate(now, engine.ResolveVector3);
            Temp3[0] = new Vector4(position, life);
            parameters["PositionConstantCount"].SetValue((float)1);
            parameters["InlinePositionConstants"].SetValue(Temp3);
            parameters["MultiplyAttributeConstant"].SetValue(MultiplyColorConstant? 1f : 0f);
            parameters["PatternTexture"].SetValue(Texture.Instance);
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
        /// Only considers the N most recently spawned particles from the source system for feedback.
        /// You can use this to avoid using old nearly-dead particles as inputs.
        /// </summary>
        public int? SlidingWindowSize = null;

        /// <summary>
        /// Waits until N additional particles are ready to consume for feedback. This provides a
        ///  way to avoid using brand-new particles.
        /// </summary>
        public int SlidingWindowMargin = 0;

        /// <summary>
        /// Spawns from randomly selected particles inside the entire sliding window instead of
        ///  only spawning from the most recent (non-consumed) particles. This spawner will no
        ///  longer consume particles.
        /// </summary>
        public bool SpawnFromEntireWindow = false;

        /// <summary>
        /// Spawns this many particles from each source particle.
        /// </summary>
        public int InstanceMultiplier = 1;

        /// <summary>
        /// Adds the position of source particles to the position Constant of new particles
        /// </summary>
        public bool AlignPositionConstant = true;

        /// <summary>
        /// The new particles inherit the source particles' velocities as a velocity constant, multiplied
        ///  by this factor
        /// </summary>
        public float SourceVelocityFactor = 0.0f;

        /// <summary>
        /// Multiplies the life of the new particle by the life of the source particle.
        /// </summary>
        public bool MultiplyLife = false;

        /// <summary>
        /// Multiplies the color Constant of new particles by the attribute of source particles
        /// </summary>
        public bool MultiplyColorConstant = false;
        
        [NonSerialized]
        private ParticleSystem.Chunk CurrentFeedbackSource;
        [NonSerialized]
        private int CurrentFeedbackSourceIndex;

        [NonSerialized]
        private Vector4[] Temp3 = new Vector4[1];

        public override void Reset () {
            base.Reset();
            CurrentFeedbackSourceIndex = 0;
            CurrentFeedbackSource = null;
        }

        public override void BeginTick (ParticleSystem system, double now, double deltaTimeSeconds, out int spawnCount, out ParticleSystem.Chunk sourceChunk) {
            // HACK
            if (InstanceMultiplier < 1)
                InstanceMultiplier = 1;

            spawnCount = 0;
            sourceChunk = null;
            CurrentFeedbackSource = null;

            if (!SourceSystem.TryInitialize(system.Engine.Configuration.SystemResolver))
                return;

            // FIXME: Support using the same system as a feedback input?
            if (SourceSystem.Instance == system)
                return;

            base.BeginTick(system, now, deltaTimeSeconds, out spawnCount, out sourceChunk);

            var requestedCount = spawnCount;

            // FIXME: We can't handle partial spawns from a source particle because tracking
            //  how many we've spawned from it is impossible without more complex state
            if ((spawnCount < InstanceMultiplier) && !SpawnFromEntireWindow) {
                AddError(spawnCount);
                spawnCount = 0;
                return;
            }

            var instances = spawnCount / InstanceMultiplier;
            var rounded = instances * InstanceMultiplier;
            if (rounded < spawnCount) {
                if (rounded > 0) {
                    AddError(spawnCount - rounded);
                    spawnCount = rounded;
                }
            }

            sourceChunk = SourceSystem.Instance.PickSourceForFeedback(instances);
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

            var maximumPossibleSpawns = availableLessMargin * InstanceMultiplier;
            spawnCount = Math.Min(spawnCount, maximumPossibleSpawns);
            // Now actually clamp it to the amount available after applying the window math that considers
            //  chunk transitions
            spawnCount = Math.Min(spawnCount, sourceChunk.AvailableForFeedback * InstanceMultiplier);
            CurrentFeedbackSource = sourceChunk;
            CurrentFeedbackSourceIndex = sourceChunk.FeedbackSourceIndex;

            // HACK: Select a random offset within the source window to pull source particles from
            //  this is not completely random but for low spawn rates it's going to look somewhat close
            if (SpawnFromEntireWindow) {
                var sourceCount = Math.Max(spawnCount / InstanceMultiplier, 1);
                CurrentFeedbackSourceIndex += RNG.Next(0, availableLessMargin - sourceCount);
            }

            // Console.WriteLine("requested {0} spawning {1}", requestedCount, spawnCount);
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
            parameters["InlinePositionConstants"].SetValue(Temp3);
            parameters["AlignPositionConstant"].SetValue(AlignPositionConstant? 1f : 0f);
            parameters["MultiplyLife"].SetValue(MultiplyLife? 1f : 0f);
            parameters["MultiplyAttributeConstant"].SetValue(MultiplyColorConstant? 1f : 0f);
            parameters["FeedbackSourceIndex"].SetValue(CurrentFeedbackSourceIndex);
            parameters["SourceVelocityFactor"].SetValue(SourceVelocityFactor);
            parameters["InstanceMultiplier"].SetValue(InstanceMultiplier);
            parameters["SpawnFromEntireWindow"].SetValue(SpawnFromEntireWindow? 1f : 0f);
        }

        public override bool IsValid {
            get {
                return true;
            }
        }
    }
}
