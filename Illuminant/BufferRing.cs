using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class BufferRing : IDisposable {
        public struct InProgressRender {
            public readonly BufferRing Ring;
            public readonly RenderTarget2D Buffer;

            internal InProgressRender (BufferRing ring, RenderTarget2D buffer) {
                Ring = ring;
                Buffer = buffer;
            }

            public bool Valid {
                get {
                    return (Buffer != null);
                }
            }

            public static implicit operator bool (InProgressRender ipr) {
                return ipr.Valid;
            }

            public void Dispose () {
                if (Buffer == null)
                    throw new InvalidOperationException();

                Ring.MarkRenderComplete(Buffer);
            }
        }

        private readonly RenderCoordinator Coordinator;
        private readonly List<RenderTarget2D> Buffers = new List<RenderTarget2D>();
        private readonly HashSet<RenderTarget2D> InProgressBuffers = new HashSet<RenderTarget2D>();

        private RenderTarget2D MostRecentValidBuffer = null;
        private ManualResetEventSlim InProgressSignal = new ManualResetEventSlim();

        public bool IsDisposed { get; private set; }

        public BufferRing (
            RenderCoordinator coordinator, int width, int height, 
            bool mipMap, SurfaceFormat format, int ringSize = 2
        ) {
            Coordinator = coordinator;

            for (int i = 0; i < ringSize; i++) {
                RenderTarget2D buffer;
                lock (coordinator.CreateResourceLock)
                    buffer = new RenderTarget2D(
                        coordinator.Device, width, height, 
                        mipMap, format, DepthFormat.None,
                        0, RenderTargetUsage.PreserveContents
                    );

                lock (Buffers)
                    Buffers.Add(buffer);
            }
        }

        public InProgressRender BeginDraw (bool allowBlocking) {
            Monitor.Enter(Buffers);
            
            while (Buffers.Count == 0) {
                if (!allowBlocking)
                    return new InProgressRender(this, null);

                Monitor.Exit(Buffers);
                InProgressSignal.Wait();
                Monitor.Enter(Buffers);
            }

            var buffer = Buffers[0];
            Buffers.RemoveAt(0);
            Buffers.Add(buffer);
            InProgressBuffers.Add(buffer);
            InProgressSignal.Reset();

            Monitor.Exit(Buffers);

            return new InProgressRender(this, buffer);
        }

        public RenderTarget2D GetBuffer (bool allowBlocking) {
            Monitor.Enter(Buffers);

            while ((MostRecentValidBuffer == null) && allowBlocking) {
                Monitor.Exit(Buffers);
                InProgressSignal.Wait();
                Monitor.Enter(Buffers);
            }

            var result = MostRecentValidBuffer;
            Monitor.Exit(Buffers);

            return result;
        }

        public void MarkRenderComplete (RenderTarget2D buffer) {
            lock (Buffers) {
                InProgressBuffers.Remove(buffer);
                MostRecentValidBuffer = buffer;
                InProgressSignal.Set();
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;

            lock (Buffers) {
                foreach (var buffer in Buffers)
                    Coordinator.DisposeResource(buffer);

                foreach (var buffer in InProgressBuffers)
                    Coordinator.DisposeResource(buffer);

                Buffers.Clear();
                InProgressBuffers.Clear();
            }
        }
    }
}
