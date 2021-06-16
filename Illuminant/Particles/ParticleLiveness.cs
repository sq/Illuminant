using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Render;

namespace Squared.Illuminant.Particles {
    public partial class ParticleSystem : IParticleSystems {
        public bool IsActive { get; set; } = true;

        // HACK: Performing occlusion queries every frame seems to be super unreliable,
        //  so just perform them intermittently and accept that our data will be outdated
        public const int LivenessCheckInterval = 4;
        private int FramesUntilNextLivenessCheck = 0; // LivenessCheckStaggerValue++ % LivenessCheckInterval;

        internal HashSet<LivenessInfo> ChunksToReap = new HashSet<LivenessInfo>();

        /// <summary>
        /// The number of frames a chunk must be dead for before it is reclaimed
        /// </summary>
        public int DeadFrameThreshold = LivenessCheckInterval * 3;

        internal class LivenessInfo {
            public Chunk          Chunk;
            public int?           Count;
            public int            DeadFrameCount;
        }

        internal LivenessInfo GetLivenessInfo (Chunk chunk) {
            LivenessInfo result;
            if (LivenessInfos.TryGetValue(chunk.ID, out result))
                return result;

            if (chunk.IsDisposed)
                return null;

            LivenessInfos.Add(
                chunk.ID, result = new LivenessInfo {
                    Chunk = chunk,
                    Count = chunk.TotalSpawned
                }
            );
            return result;
        }

        internal void ProcessLatestLivenessInfo (Chunk chunk) {
            var threshold = DeadFrameThreshold;
            /*
            // HACK: Keep a chunk around for a very long time if we don't have any others.
            // FIXME: This prevents a hitch when spawning new particles and also works 
            //  around a bug (?) where our last remaining chunk is reaped too early.
            if (Chunks.Count == 1)
                threshold = 180;
            */

            LivenessInfo li;
            lock (LivenessInfos)
                li = LivenessInfos[chunk.ID];

            // Console.WriteLine("{0} count = {1}", chunk.ID, li.Count);

            if (!li.Count.HasValue)
                return;

            if (li.Count.Value <= 0) {
                li.DeadFrameCount++;
            } else {
                li.DeadFrameCount = 0;
            }

            bool isDead = (li.DeadFrameCount >= threshold);
            if (isDead) {
                // Console.WriteLine("Reaping {0}", chunk.ID);
                lock (ChunksToReap)
                    ChunksToReap.Add(li);
            }
        }

        private void UpdateLiveCountAndReapDeadChunks () {
            // FIXME: LiveCount randomly drops to 0 when a chunk is reaped
            var oldLiveCount = LiveCount;
            LiveCount = 0;

            lock (LivenessInfos)
            foreach (var kvp in LivenessInfos) {
                var li = kvp.Value;

                var chunkCount = li.Count.GetValueOrDefault(0);
                if (Engine.Configuration.AccurateLivenessCounts)
                    LiveCount += chunkCount;
                else
                    LiveCount += (chunkCount > 0) ? 1 : 0;
            }

            lock (ChunksToReap) {
                foreach (var li in ChunksToReap) {
                    LivenessInfos.Remove(li.Chunk.ID);
                    Reap(li.Chunk);
                }

                if (ChunksToReap.Count > 0)
                    ChunksToReap.Clear();
            }
        }

        private void Reap (BufferSet buffer) {
            if (buffer == null)
                return;
            if (buffer.Size != Engine.Configuration.ChunkSize)
                Engine.Coordinator.DisposeResource(buffer);
            else
                Engine.DiscardedBuffers.Add(buffer);
        }

        private void Reap (Chunk chunk) {
            // Console.WriteLine("Chunk reaped");
            Reap(chunk.Previous);
            Reap(chunk.Current);
            chunk.Previous = chunk.Current = null;
            lock (Chunks)
                Chunks.Remove(chunk);
            chunk.Clear();
            Engine.Coordinator.DisposeResource(chunk);
        }

        private void ComputeLiveness (
            IBatchContainer group, int layer
        ) {
            lock (Chunks)
            if (Chunks.Count == 0)
                return;

            lock (Engine.LivenessQueryRequests)
                Engine.LivenessQueryRequests.Add(this);
        }
    }
}
