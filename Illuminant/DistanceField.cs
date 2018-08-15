using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class SliceInfo {
        public int ValidSliceCount { get; internal set; }
        internal readonly List<int> InvalidSlices = new List<int>();
    }

    public class DistanceField : IDisposable {
        public bool IsDisposed { get; private set; }

        public readonly float VirtualWidth, VirtualHeight, VirtualDepth;
        public readonly float Resolution;
        public readonly float MaximumEncodedDistance;

        public readonly RenderTarget2D Texture;
        public readonly int SliceWidth, SliceHeight, SliceCount;
        public readonly int PhysicalSliceCount;
        public readonly int ColumnCount, RowCount;

        private readonly object UseLock;

        internal SliceInfo SliceInfo = new SliceInfo();

        public DistanceField (
            RenderCoordinator coordinator,
            float virtualWidth, float virtualHeight, float virtualDepth,
            int sliceCount, float resolution = 1f, float maximumEncodedDistance = 256f
        ) {
            VirtualWidth = virtualWidth;
            VirtualHeight = virtualHeight;
            VirtualDepth = virtualDepth;
            Resolution = resolution;
            MaximumEncodedDistance = maximumEncodedDistance;

            SliceWidth = (int)Math.Ceiling(virtualWidth * resolution);
            SliceHeight = (int)Math.Ceiling(virtualHeight * resolution);
            int maxSlicesX = 4096 / SliceWidth;
            int maxSlicesY = 4096 / SliceHeight;
            int maxSlices = maxSlicesX * maxSlicesY * LightingRenderer.PackedSliceCount;
                
            // HACK: If they ask for too many slices we give them as many as we can.
            SliceCount = Math.Min(sliceCount, maxSlices);

            PhysicalSliceCount = (int)Math.Ceiling(SliceCount / (float)LightingRenderer.PackedSliceCount);

            ColumnCount = Math.Min(maxSlicesX, PhysicalSliceCount);
            RowCount = Math.Max((int)Math.Ceiling(PhysicalSliceCount / (float)maxSlicesX), 1);

            UseLock = coordinator.UseResourceLock;

            lock (coordinator.CreateResourceLock)
                Texture = new RenderTarget2D(
                    coordinator.Device,
                    SliceWidth * ColumnCount, 
                    SliceHeight * RowCount,
                    false, SurfaceFormat.Rgba64,
                    DepthFormat.None, 0, 
                    RenderTargetUsage.PreserveContents
                );

            coordinator.DeviceReset += Coordinator_DeviceReset;

            Invalidate();
        }

        public virtual bool IsFullyGenerated {
            get {
                return (SliceInfo.ValidSliceCount >= SliceCount) &&
                    (SliceInfo.InvalidSlices.Count == 0);
            }
        }

        public virtual bool NeedsRasterize {
            get {
                return SliceInfo.InvalidSlices.Count > 0;
            }
        }

        internal Vector3 GetExtent3 (float maximumZ) {
            return new Vector3(
                VirtualWidth,
                VirtualHeight,
                maximumZ
            );
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            if (IsDisposed)
                return;

            SliceInfo.ValidSliceCount = 0;
            SliceInfo.InvalidSlices.Clear();
            for (var i = 0; i < SliceCount; i++)
                SliceInfo.InvalidSlices.Add(i);
        }

        public string Name {
            get {
                return Render.Tracing.ObjectNames.ToObjectID(Texture);
            }
            set {
                Render.Tracing.ObjectNames.SetName(Texture, value);
            }
        }

        public void Save (string output) {
            using (var stream = File.OpenWrite(output))
                Save(stream);
        }

        public virtual void Save (Stream output) {
            if (SliceInfo.ValidSliceCount < SliceCount)
                throw new InvalidOperationException("The distance field must be fully valid");

            var size = 8 * Texture.Width * Texture.Height;
            var data = new byte[size];

            lock (UseLock)
                Texture.GetData(data);

            output.Write(data, 0, size);
        }

        public void Load (string path) {
            using (var stream = File.OpenRead(path))
                Load(stream);
        }

        public virtual void Load (Stream input) {
            var size = 8 * Texture.Width * Texture.Height;
            var data = new byte[size];
            var bytesRead = input.Read(data, 0, size);
            if (bytesRead != size)
                throw new Exception("Truncated file");

            lock (UseLock)
                Texture.SetData(data);

            SliceInfo.InvalidSlices.Clear();
            // FIXME: Is this right?
            SliceInfo.ValidSliceCount = ((SliceCount + 2) / 3) * 3;
        }

        public virtual void Invalidate () {
            for (var i = 0; i < SliceCount; i++) {
                if (SliceInfo.InvalidSlices.Contains(i))
                    continue;

                SliceInfo.InvalidSlices.Add(i);
            }
        }

        public virtual void ValidateSlice (int index) {
            SliceInfo.InvalidSlices.Remove(index);
        }

        public virtual void MarkValidSlice (int index) {
            SliceInfo.ValidSliceCount = Math.Max(SliceInfo.ValidSliceCount, index);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            // TODO: Remove event listener from rendercoordinator

            IsDisposed = true;
            Texture.Dispose();
        }
    }

    public class DynamicDistanceField : DistanceField {
        SliceInfo StaticSliceInfo = new SliceInfo();

        public readonly RenderTarget2D StaticTexture;

        public DynamicDistanceField (
            RenderCoordinator coordinator,
            float virtualWidth, float virtualHeight, float virtualDepth,
            int sliceCount, float resolution = 1f, float maximumEncodedDistance = 256f
        ) : base (coordinator, virtualWidth, virtualHeight, virtualDepth, sliceCount, resolution, maximumEncodedDistance) {
            lock (coordinator.CreateResourceLock)
                StaticTexture = new RenderTarget2D(
                    coordinator.Device,
                    SliceWidth * ColumnCount, 
                    SliceHeight * RowCount,
                    false, SurfaceFormat.Rgba64,
                    DepthFormat.None, 0, 
                    RenderTargetUsage.PreserveContents
                );
        }

        public override void Invalidate () {
            Invalidate(true, true);
        }

        public void Invalidate (bool invalidateStatic, bool invalidateDynamic) {
            for (var i = 0; i < SliceCount; i++) {
                if (invalidateDynamic && !SliceInfo.InvalidSlices.Contains(i))
                    SliceInfo.InvalidSlices.Add(i);
                if (invalidateStatic && !StaticSliceInfo.InvalidSlices.Contains(i))
                    StaticSliceInfo.InvalidSlices.Add(i);
            }
        }

        public override void ValidateSlice (int index) {
            SliceInfo.InvalidSlices.Remove(index);
            StaticSliceInfo.InvalidSlices.Remove(index);
        }

        public override void MarkValidSlice (int index) {
            SliceInfo.ValidSliceCount = Math.Max(SliceInfo.ValidSliceCount, index);
            StaticSliceInfo.ValidSliceCount = Math.Max(StaticSliceInfo.ValidSliceCount, index);
        }

        public override void Load (Stream input) {
            throw new NotImplementedException();
        }

        public override void Save (Stream output) {
            throw new NotImplementedException();
        }
    }
}
