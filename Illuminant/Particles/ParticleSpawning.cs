using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Render;

namespace Squared.Illuminant.Particles {
    public partial class ParticleSystem : IParticleSystems {
        // Make sure to lock the slice first.
        public int InitializeNewChunks<TElement> (
            int particleCount,
            RenderManager renderManager,
            bool parallel,
            ParticleBufferInitializer<TElement> positionInitializer,
            ParticleBufferInitializer<TElement> velocityInitializer,
            ParticleBufferInitializer<TElement> colorInitializer
        ) where TElement : struct {
            var mc = ChunkMaximumCount;
            int numToSpawn = (int)Math.Ceiling((double)particleCount / mc);

            var g = parallel ? Engine.Coordinator.ThreadGroup : null;

            for (int i = 0; i < numToSpawn; i++) {
                var c = CreateChunk();
                if (c == null)
                    return 0;

                // RotateBuffers(c, renderManager.DeviceManager.FrameIndex);
                // Console.WriteLine("Creating new chunk " + c.ID);
                var offset = i * mc;
                var curr = c.Current;
                var prev = c.Previous;
                var pos = new BufferInitializer<TElement> { Buffer = curr.PositionAndLife, Buffer2 = prev?.PositionAndLife, Initializer = positionInitializer, Offset = offset };
                var vel = new BufferInitializer<TElement> { Buffer = curr.Velocity, Buffer2 = prev?.Velocity, Initializer = velocityInitializer, Offset = offset };
                var attr = new BufferInitializer<TElement> { Buffer = c.Color, Initializer = colorInitializer, Offset = offset };
                var job = new ChunkInitializer<TElement> {
                    System = this,
                    Position = pos,
                    Velocity = vel,
                    Color = attr,
                    Chunk = c,
                    Remaining = (colorInitializer != null) ? 3 : 2
                };
                c.TotalSpawned = ChunkMaximumCount;

                var li = GetLivenessInfo(c);
                if (li != null) {
                    li.Count = ChunkMaximumCount;
                    ProcessLatestLivenessInfo(c);
                }

                job.Run(g);
            }

            return numToSpawn * mc;
        }

        public int Spawn (
            int particleCount,
            ParticleBufferInitializer<Vector4> positionInitializer,
            ParticleBufferInitializer<Vector4> velocityInitializer,
            bool parallel = true
        ) {
            return Spawn(particleCount, positionInitializer, velocityInitializer, null, parallel);
        }

        public int Spawn (
            int particleCount,
            ParticleBufferInitializer<Vector4> positionInitializer,
            ParticleBufferInitializer<Vector4> velocityInitializer,
            ParticleBufferInitializer<Vector4> colorInitializer,
            bool parallel = true
        ) {
            var result = InitializeNewChunks(
                particleCount,
                Engine.Coordinator.Manager,
                parallel,
                positionInitializer,
                velocityInitializer,
                colorInitializer
            );
            return result;
        }

        public int Spawn (
            int particleCount,
            ParticleBufferInitializer<HalfVector4> positionInitializer,
            ParticleBufferInitializer<HalfVector4> velocityInitializer,
            bool parallel = true
        ) {
            return Spawn(particleCount, positionInitializer, velocityInitializer, null, parallel);
        }

        public int Spawn (
            int particleCount,            
            ParticleBufferInitializer<HalfVector4> positionInitializer,
            ParticleBufferInitializer<HalfVector4> velocityInitializer,
            ParticleBufferInitializer<HalfVector4> colorInitializer,
            bool parallel = true
        ) {
            var result = InitializeNewChunks(
                particleCount,
                Engine.Coordinator.Manager,
                parallel,
                positionInitializer,
                velocityInitializer,
                colorInitializer
            );
            return result;
        }

        private bool RunSpawner (
            IBatchContainer container, ref int layer,
            long startedWhen, Transforms.SpawnerBase spawner,
            double deltaTimeSeconds, double now, bool isSecondPass
        ) {
            int spawnCount = 0, requestedSpawnCount;

            if (!spawner.IsValid)
                return false;

            Chunk sourceChunk;
            spawner.BeginTick(this, now, deltaTimeSeconds, out requestedSpawnCount, out sourceChunk);

            if (requestedSpawnCount <= 0) {
                return false;
            } else if (requestedSpawnCount > ChunkMaximumCount)
                spawnCount = ChunkMaximumCount;
            else
                spawnCount = requestedSpawnCount;

            bool needClear;
            var fs = spawner as Transforms.FeedbackSpawner;
            var chunk = PickTargetForSpawn(fs != null, spawnCount, out needClear, spawner.PartialSpawnAllowed);
            if (chunk == null)
                return false;

            if (spawnCount > chunk.Free) {
                if (spawner.PartialSpawnAllowed)
                    spawnCount = chunk.Free;
                else
                    return false;
            }

            if (chunk == null)
                throw new Exception("Failed to locate or create a chunk to spawn in");

            var first = chunk.NextSpawnOffset;
            var last = chunk.NextSpawnOffset + spawnCount - 1;
            spawner.SetIndices(first, last);

            chunk.NextSpawnOffset += spawnCount;
            TotalSpawnCount += spawnCount;
            if (sourceChunk != null) {
                var consumedCount = spawnCount;
                // HACK
                if ((fs != null) && !fs.SpawnFromEntireWindow) {
                    consumedCount = Math.Max(consumedCount / fs.InstanceMultiplier, 1);
                    sourceChunk.TotalConsumedForFeedback += consumedCount;
                }
            }

            // Console.WriteLine("Spawning {0} into {1} (w/{2} free)", spawnCount, chunk.ID, chunk.Free);
            spawner.EndTick(requestedSpawnCount, spawnCount);
            chunk.TotalSpawned += spawnCount;

            if (spawnCount > 0) {
                var li = GetLivenessInfo(chunk);
                if (li != null)
                    li.DeadFrameCount = 0;
            }

            if (spawnCount > 0) {
                var h = isSecondPass ? spawner.Handler2 : spawner.Handler;

                RunTransform(
                    chunk, container, ref layer, ((Transforms.IParticleTransform)spawner).GetMaterial(Engine.ParticleMaterials),
                    startedWhen, true, false,
                    h.BeforeDraw, h.AfterDraw, 
                    deltaTimeSeconds, needClear, now, false,
                    sourceChunk, spawner.Label
                );
            }

            chunk.ApproximateMaximumLife = Math.Max(
                chunk.ApproximateMaximumLife,
                spawner.EstimateMaximumLifeForNewParticle((float)now, Engine.ResolveSingle)
            );

            var isPartialSpawn = (requestedSpawnCount > spawnCount);
            return isPartialSpawn;
        }

        internal Chunk PickTargetForSpawn (
            bool feedback, int count, 
            ref int currentTarget, out bool needClear,
            bool partialSpawnAllowed
        ) {
            var chunk = ChunkFromID(currentTarget);
            // FIXME: Ideally we could split the spawn across this chunk and an old one.
            if (chunk != null) {
                if (chunk.Free < (partialSpawnAllowed ? 16 : count)) {
                    chunk.NoLongerASpawnTarget = true;
                    currentTarget = -1;
                    chunk = null;
                }
            }

            if (chunk == null) {
                chunk = CreateChunk();
                if (chunk == null) {
                    needClear = false;
                    return null;
                }

                chunk.IsFeedbackSource = feedback;
                currentTarget = chunk.ID;
                lock (Chunks)
                    Chunks.Add(chunk);
                needClear = true;
            } else {
                needClear = false;
            }

            return chunk;
        }

        internal Chunk GetCurrentSpawnTarget (bool feedback) {
            return ChunkFromID(feedback ? CurrentFeedbackSpawnTarget : CurrentSpawnTarget);
        }

        internal Chunk PickTargetForSpawn (bool feedback, int count, out bool needClear, bool partialSpawnAllowed) {
            if (feedback)
                return PickTargetForSpawn(true, count, ref CurrentFeedbackSpawnTarget, out needClear, partialSpawnAllowed);
            else
                return PickTargetForSpawn(false, count, ref CurrentSpawnTarget, out needClear, partialSpawnAllowed);
        }

        internal Chunk PickSourceForFeedback (int count) {
            var cfs = ChunkFromID(CurrentFeedbackSource);
            if (cfs != null) {
                if ((cfs.AvailableForFeedback <= 0) && (cfs.Free <= 0))
                    cfs = null;
            }
            lock (Chunks) {
                Chunk newChunk = null;
                foreach (var c in Chunks) {
                    if ((c.AvailableForFeedback >= count / 2) && !c.IsFeedbackSource) {
                        newChunk = c;
                        break;
                    }
                }

                if (newChunk != null)
                    CurrentFeedbackSource = newChunk.ID;

                return newChunk;
            }
        }
    }
}
