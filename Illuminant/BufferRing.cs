using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Util;

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
        
        private long           MostRecentValidBufferTimestamp;
        private RenderTarget2D MostRecentValidBuffer = null;
        private ManualResetEventSlim InProgressSignal = new ManualResetEventSlim();

        public bool IsDisposed { get; private set; }
        public int RingSize { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool MipMap { get; private set; }
        public SurfaceFormat Format { get; private set; }
        public DepthFormat DepthFormat { get; private set; }

        public readonly string Name;

        public BufferRing (
            RenderCoordinator coordinator, int width, int height, 
            bool mipMap, SurfaceFormat format, DepthFormat depthFormat = DepthFormat.None, int ringSize = 2,
            string name = null
        ) {
            Coordinator = coordinator;

            RingSize = ringSize;
            MipMap = mipMap;
            Format = format;
            DepthFormat = depthFormat;
            Name = name;

            CreateBuffers(width, height);
        }

        private void CreateBuffers (int width, int height) {
            Width = width;
            Height = height;

            for (int i = 0; i < RingSize; i++) {
                RenderTarget2D buffer;
                lock (Coordinator.CreateResourceLock) {
                    buffer = new RenderTarget2D(
                        Coordinator.Device, width, height,
                        MipMap, Format, DepthFormat,
                        0, RenderTargetUsage.PreserveContents
                    ) {
                        Name = $"{Name ?? "BufferRing"} buffer #{i}"
                    };

                    Coordinator.RegisterAutoAllocatedTextureResource(buffer);
                }

                lock (Buffers) {
                    if (Buffers.Count > i) {
                        Coordinator.DisposeResource(Buffers[i]);
                        Buffers[i] = buffer;
                    } else
                        Buffers.Add(buffer);
                }
            }
        }

        public void ResizeBuffers (int width, int height) {
            if ((width == Width) && (height == Height))
                return;

            CreateBuffers(width, height);
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

        public RenderTarget2D GetBuffer (bool allowBlocking, out long timestamp) {
            Monitor.Enter(Buffers);

            while ((MostRecentValidBuffer == null) && allowBlocking) {
                Monitor.Exit(Buffers);
                InProgressSignal.Wait();
                Monitor.Enter(Buffers);
            }

            var result = MostRecentValidBuffer;
            timestamp = MostRecentValidBufferTimestamp;
            Monitor.Exit(Buffers);

            return result;
        }

        public void MarkRenderComplete (RenderTarget2D buffer) {
            lock (Buffers) {
                InProgressBuffers.Remove(buffer);
                MostRecentValidBuffer = buffer;
                MostRecentValidBufferTimestamp = Time.Ticks;
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
