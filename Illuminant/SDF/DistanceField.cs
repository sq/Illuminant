using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Evil;

namespace Squared.Illuminant {
    public class SliceInfo {
        public int ValidSliceCount { get; internal set; }
        internal readonly List<int> InvalidSlices = new List<int>();
    }

    public class DistanceField : IDisposable {
#if FNA
        public const int MaxSurfaceSize = 8192;
#else
        public const int MaxSurfaceSize = 4096;
#endif

        public const int DefaultMaximumEncodedDistance = 128;

        public const SurfaceFormat Format = SurfaceFormat.Rgba64;

        public bool IsDisposed { get; private set; }

        public readonly int VirtualWidth, VirtualHeight;
        public readonly float VirtualDepth;
        public readonly double Resolution;
        public readonly int MaximumEncodedDistance;

        public readonly RenderCoordinator Coordinator;
        public readonly AutoRenderTarget Texture;
#if DF3D
        public readonly Texture3D Texture3D;
#endif
        public readonly int SliceWidth, SliceHeight, SliceCount;
        public readonly int PhysicalSliceCount;
        public readonly int ColumnCount, RowCount;

        internal bool NeedClear;

        private readonly object UseLock;

        internal readonly SliceInfo SliceInfo = new SliceInfo();

        public DistanceField (
            RenderCoordinator coordinator,
            int virtualWidth, int virtualHeight, float virtualDepth,
            int requestedSliceCount, double requestedResolution = 1, 
            int maximumEncodedDistance = DefaultMaximumEncodedDistance
        ) {
            Coordinator = coordinator;
            VirtualWidth = virtualWidth;
            VirtualHeight = virtualHeight;
            VirtualDepth = virtualDepth;
            MaximumEncodedDistance = maximumEncodedDistance;

            if (requestedResolution < 0.05)
                requestedResolution = 0.05;
            else if (requestedResolution > 1)
                requestedResolution = 1;

            var candidateSliceWidth = (int)Math.Round(VirtualWidth * requestedResolution);
            var candidateSliceHeight = (int)Math.Round(VirtualHeight * requestedResolution);

            var fracX = (double)VirtualWidth / candidateSliceWidth;
            var fracY = (double)VirtualHeight / candidateSliceHeight;
            var frac = (fracX + fracY) / 2;

            var resolution = Math.Round(1.0 / frac, 3);
            if (resolution < 0.05)
                resolution = 0.05;
            else if (resolution > 1)
                resolution = 1;

            Resolution = resolution;

            SliceWidth = (int)Math.Round(VirtualWidth * Resolution);
            SliceHeight = (int)Math.Round(VirtualHeight * Resolution);

            int maxSlicesX = MaxSurfaceSize / SliceWidth;
            int maxSlicesY = MaxSurfaceSize / SliceHeight;
            int maxSlices = (maxSlicesX * maxSlicesY * LightingRenderer.PackedSliceCount);

            var sliceCount = Math.Max(3, requestedSliceCount);
            sliceCount = (((sliceCount + 2) / 3) * 3);

            SliceCount = Math.Min(sliceCount, maxSlices);
            PhysicalSliceCount = (int)Math.Ceiling(SliceCount / (float)LightingRenderer.PackedSliceCount);

            // Console.WriteLine("{0} -> {1} -> {2}", requestedSliceCount, sliceCount, PhysicalSliceCount);

            ColumnCount = Math.Min(maxSlicesX, PhysicalSliceCount);
            RowCount = Math.Min(maxSlicesY, Math.Max((int)Math.Ceiling(PhysicalSliceCount / (float)maxSlicesX), 1));

            // HACK: If the DF is going to be extremely wide but not tall, rebalance it
            // so that it is easier to examine instead of being 4096x128 or whatever
            while ((RowCount < ColumnCount) && (RowCount < maxSlicesY)) {
                var newRowCount = RowCount + 1;
                var newColumnCount = (int)Math.Ceiling(PhysicalSliceCount / (float)newRowCount);

                if (newRowCount > maxSlicesX)
                    newRowCount = maxSlicesX;
                if (newColumnCount > maxSlicesY)
                    newColumnCount = maxSlicesY;

                if ((newRowCount * newColumnCount) < PhysicalSliceCount)
                    break;
                RowCount = newRowCount;
                ColumnCount = newColumnCount;
            }

            UseLock = coordinator.UseResourceLock;

            lock (coordinator.CreateResourceLock)
                Texture = new AutoRenderTarget(
                    coordinator,
                    SliceWidth * ColumnCount,
                    SliceHeight * RowCount,
                    false, Format, name: "DistanceField.Texture"
                );

#if DF3D
            lock (coordinator.CreateResourceLock)
                Texture3D = new Texture3D(
                    coordinator.Device,
                    SliceWidth, 
                    SliceHeight,
                    SliceCount,
                    false, SurfaceFormat.Rg32
                );
#endif

            coordinator.DeviceReset += Coordinator_DeviceReset;
            NeedClear = true;

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

        internal Vector4 GetExtent4 (float maximumZ) {
            return new Vector4(
                VirtualWidth,
                VirtualHeight,
                maximumZ,
                MaximumEncodedDistance
            );
        }

        protected void DeviceResetImpl (SliceInfo sliceInfo) {
            NeedClear = true;
            SliceInfo.ValidSliceCount = 0;
            SliceInfo.InvalidSlices.Clear();
            for (var i = 0; i < SliceCount; i++)
                SliceInfo.InvalidSlices.Add(i);
        }

        protected virtual void Coordinator_DeviceReset (object sender, EventArgs e) {
            if (IsDisposed)
                return;

            DeviceResetImpl(SliceInfo);
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

            var tex = Texture.Get();

            lock (UseLock)
                tex.GetData(data);

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

            var tex = Texture.Get();

            lock (UseLock)
                tex.SetData(data);

            lock (UseLock)
                Update3DTexture();

            SliceInfo.InvalidSlices.Clear();
            // FIXME: Is this right?
            SliceInfo.ValidSliceCount = ((SliceCount + 2) / 3) * 3;
        }

        public void Update3DTexture () {
            Update3DTexture(0, SliceCount);
        }

        public unsafe void Update3DTexture (int firstSlice, int count) {
#if DF3D
            // FIXME
            int numComponents;
            var sizeofPixel = Render.Evil.TextureUtils.GetBytesPerPixelAndComponents(Format, out numComponents);
            byte[] srcBuffer = new byte[sizeofPixel * SliceWidth * SliceHeight],
                destBuffer = new byte[sizeof(ushort) * 2 * SliceWidth * SliceHeight];
            var tex = Texture.Get();
            var lastUpdatedPhysicalSlice = -1;
            for (int i = 0; i < count; i++) {
                var sliceIndex = firstSlice + i;
                var physicalSliceIndex = sliceIndex / 3;
                if (physicalSliceIndex <= lastUpdatedPhysicalSlice)
                    continue;
                var columnIndex = physicalSliceIndex % ColumnCount;
                var rowIndex = physicalSliceIndex / ColumnCount;
                var x1 = columnIndex * SliceWidth;
                var y1 = rowIndex * SliceHeight;
                var rect = new Rectangle(
                    x1, y1, SliceWidth, SliceHeight
                );

                lastUpdatedPhysicalSlice = Math.Max(lastUpdatedPhysicalSlice, physicalSliceIndex);

                fixed (byte* pSrcBuffer = srcBuffer)
                fixed (byte* pDestBuffer = destBuffer) {
                    IntPtr iSrc = new IntPtr(pSrcBuffer),
                        iDest = new IntPtr(pDestBuffer);

                    lock (UseLock)
                        tex.GetDataPointerEXT(0, rect, iSrc, srcBuffer.Length);

                    for (int j = 0; j < 3; j++) {
                        for (int y = 0; y < SliceHeight; y++) {
                            var pPackedSrc = ((ushort*)pSrcBuffer) + (y * SliceWidth * 4);
                            var pDest = ((ushort*)pDestBuffer) + (y * SliceWidth * 2);

                            for (int x = 0; x < SliceWidth; x++) {
                                int k = (x * 4) + j;
                                pDest[(x * 2)] = pPackedSrc[k];
                            }
                        }

                        int top = (physicalSliceIndex * 3) + j;
                        lock (UseLock)
                            Texture3D.SetDataPointerEXT(0, 0, 0, SliceWidth, SliceHeight, top, top + 1, iDest, destBuffer.Length);
                    }
                }
            }
#endif
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

        protected virtual void DisposeResources () {
            Coordinator.DisposeResource(Texture);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            // TODO: Remove event listener from rendercoordinator

            IsDisposed = true;

            DisposeResources();
        }
    }

    public class DynamicDistanceField : DistanceField {
        internal readonly SliceInfo StaticSliceInfo = new SliceInfo();
        public readonly AutoRenderTarget StaticTexture;

        public DynamicDistanceField (
            RenderCoordinator coordinator,
            int virtualWidth, int virtualHeight, float virtualDepth,
            int sliceCount, double requestedResolution = 1, int maximumEncodedDistance = DefaultMaximumEncodedDistance
        ) : base (coordinator, virtualWidth, virtualHeight, virtualDepth, sliceCount, requestedResolution, maximumEncodedDistance) {
            lock (coordinator.CreateResourceLock)
                StaticTexture = new AutoRenderTarget(
                    coordinator,
                    SliceWidth * ColumnCount, 
                    SliceHeight * RowCount,
                    false, SurfaceFormat.Rgba64,
                    name: "DynamicDistanceField"
                );
        }

        public override void Invalidate () {
            Invalidate(true);
        }

        public void Invalidate (bool invalidateStatic) {
            for (var i = 0; i < SliceCount; i++) {
                if (!SliceInfo.InvalidSlices.Contains(i))
                    SliceInfo.InvalidSlices.Add(i);
                if (invalidateStatic && !StaticSliceInfo.InvalidSlices.Contains(i))
                    StaticSliceInfo.InvalidSlices.Add(i);
            }
        }

        public void ValidateSlice (int index, bool dynamic) {
            if (dynamic && !StaticSliceInfo.InvalidSlices.Contains(index))
                SliceInfo.InvalidSlices.Remove(index);
            else
                StaticSliceInfo.InvalidSlices.Remove(index);
        }

        public void MarkValidSlice (int index, bool dynamic) {
            if (dynamic)
                SliceInfo.ValidSliceCount = Math.Min(Math.Max(SliceInfo.ValidSliceCount, index), StaticSliceInfo.ValidSliceCount);
            else
                StaticSliceInfo.ValidSliceCount = Math.Max(StaticSliceInfo.ValidSliceCount, index);
        }

        public override void ValidateSlice (int index) {
            ValidateSlice(index, false);
            ValidateSlice(index, true);
        }

        public override void MarkValidSlice (int index) {
            MarkValidSlice(index, false);
            MarkValidSlice(index, true);
        }

        public override void Load (Stream input) {
            throw new NotImplementedException();
        }

        public override void Save (Stream output) {
            throw new NotImplementedException();
        }

        protected override void Coordinator_DeviceReset (object sender, EventArgs e) {
            base.Coordinator_DeviceReset(sender, e);
            DeviceResetImpl(StaticSliceInfo);
        }

        protected override void DisposeResources () {
            base.DisposeResources();
            Coordinator.DisposeResource(StaticTexture);
        }
    }
}
