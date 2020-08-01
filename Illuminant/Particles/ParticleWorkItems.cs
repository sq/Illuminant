using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Render;
using Squared.Render.Evil;
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant.Particles {
    public partial class ParticleSystem : IParticleSystems {
        internal class ChunkInitializer<TElement>
            where TElement : struct {
            public ParticleSystem System;
            public int Remaining;
            public BufferInitializer<TElement> Position, Velocity, Color;
            public Chunk Chunk;
            public bool HasFailed;

            public void Run (ThreadGroup g) {
                if (g != null) {
                    var q = g.GetQueueForType<BufferInitializer<TElement>>();
                    Position.Parent = Velocity.Parent = Color.Parent = this;

                    q.Enqueue(ref Position);
                    q.Enqueue(ref Velocity);
                    if (Color.Initializer != null)
                        q.Enqueue(ref Color);
                } else {
                    Position.Execute();
                    Velocity.Execute();
                    if (Color.Initializer != null)
                        Color.Execute();
                }
            }

            public void OnBufferInitialized (bool failed) {
                var result = Interlocked.Decrement(ref Remaining);
                if (failed)
                    HasFailed = true;

                if (result == 0) {
                    if (!failed)
                    lock (System.NewUserChunks)
                        System.NewUserChunks.Add(Chunk);
                }
            }
        }

#if FNA
        // FIXME: We should be able to separate out the upload operation, I think?
        internal struct BufferInitializer<TElement> : IMainThreadWorkItem
#else
        internal struct BufferInitializer<TElement> : IWorkItem
#endif
            where TElement : struct
        {
            static ThreadLocal<TElement[]> Scratch = new ThreadLocal<TElement[]>();

            public ParticleBufferInitializer<TElement> Initializer;
            public int Offset;
            public RenderTarget2D Buffer, Buffer2;
            public ChunkInitializer<TElement> Parent;

            public void Execute () {
                var scratch = Scratch.Value;
                if (scratch == null)
                    Scratch.Value = scratch = new TElement[Parent.System.ChunkMaximumCount];

                var maxLife = Initializer(scratch, Offset);

                try {                    
                    if (!Parent.Chunk.IsDisposed)
                    lock (Parent.System.Engine.Coordinator.UseResourceLock) {
                        if (AutoRenderTargetBase.IsRenderTargetValid(Buffer))
                            Buffer.SetData(scratch);
                        if (AutoRenderTargetBase.IsRenderTargetValid(Buffer2))
                            Buffer2.SetData(scratch);
                    }

                    float oldApproximateMaximumLife, newApproximateMaximumLife;
                    do {
                        oldApproximateMaximumLife = Parent.Chunk.ApproximateMaximumLife;
                        newApproximateMaximumLife = Math.Max(oldApproximateMaximumLife, maxLife);
                    } while (Interlocked.CompareExchange(ref Parent.Chunk.ApproximateMaximumLife, newApproximateMaximumLife, oldApproximateMaximumLife) != oldApproximateMaximumLife);

                    Parent.OnBufferInitialized(false);
                } catch (ObjectDisposedException) {
                    // This can happen even if we properly synchronize accesses, 
                    //  presumably because the owning graphicsdevice got eaten :(
                    Parent.OnBufferInitialized(true);
                }
            }
        }
    }

    public partial class ParticleEngine : IDisposable {

#if FNA
        private struct LivenessDataReadbackWorkItem : Threading.IMainThreadWorkItem {
#else
        private struct LivenessDataReadbackWorkItem : Threading.IWorkItem {
#endif
            public BufferPool<ParticleSystem.Chunk>.Buffer Chunks;
            public RenderTarget2D RenderTarget;
            public ParticleEngine Engine;
            public bool NeedResourceLock;
            public int ResetCount;

            public void Execute () {
                if (ResetCount != Engine.ResetCount) {
                    Chunks.Dispose();
                    Console.WriteLine("A reset invalidated this query");
                    return;
                }

                if (!AutoRenderTargetBase.IsRenderTargetValid(RenderTarget)) {
                    Chunks.Dispose();
                    Console.WriteLine("Invalid render target");
                    return;
                }

                // Console.WriteLine("Read liveness data texture");

                var startedWhen = Time.Ticks;
                using (var buffer = Squared.Util.BufferPool<Rg32>.Allocate(RenderTarget.Width)) {
                    if (NeedResourceLock)
                        Monitor.Enter(Engine.Coordinator.UseResourceLock);
                    try {
                        var device = Engine.Coordinator.Device;
                        RenderTrace.ImmediateMarker(device, "Read liveness data from previous frame");
                        RenderTarget.GetDataFast(buffer.Data);
                        var elapsedMs = (Time.Ticks - startedWhen) / (double)Time.MillisecondInTicks;
                        RenderTrace.ImmediateMarker(device, "Readback took {0:000.0}ms", elapsedMs);
                    } finally {
                        if (NeedResourceLock)
                            Monitor.Exit(Engine.Coordinator.UseResourceLock);
                    }

                    Engine.ProcessLivenessInfoData(buffer.Data, Chunks.Data);
                    Chunks.Dispose();
                }

            }
        }
    }
}
