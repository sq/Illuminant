using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Tracing;
using Squared.Threading.CoreCLR;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
	    [StructLayout(LayoutKind.Sequential, Pack = 1)]
	    public struct VertexPositionVector4 : IVertexType
	    {

		    VertexDeclaration IVertexType.VertexDeclaration
		    {
			    get
			    {
				    return VertexDeclaration;
			    }
		    }

		    public Vector3 Position;

		    public Vector4 Color;

		    public static readonly VertexDeclaration VertexDeclaration;

		    static VertexPositionVector4()
		    {
			    VertexDeclaration = new VertexDeclaration(
				    new []
				    {
					    new VertexElement(
						    0,
						    VertexElementFormat.Vector3,
						    VertexElementUsage.Position,
						    0
					    ),
					    new VertexElement(
						    12,
						    VertexElementFormat.Vector4,
						    VertexElementUsage.Color,
						    0
					    )
				    }
			    );
		    }

		    public VertexPositionVector4(Vector3 position, Vector4 color)
		    {
			    Position = position;
			    Color = color;
		    }
	    }
        
        private void RenderDistanceField (ref int layerIndex, IBatchContainer resultGroup) {
            if (_DistanceField == null)
                return;

            if (_DistanceField is DynamicDistanceField) {
                RenderDistanceFieldPartition(ref layerIndex, resultGroup, false);
                // FIXME: Don't allow a dynamic slice to be flagged as valid unless the static slice is also valid
                RenderDistanceFieldPartition(ref layerIndex, resultGroup, true);
            } else {
                RenderDistanceFieldPartition(ref layerIndex, resultGroup, null);
            }
        }

        private float SliceIndexToZ (int slice) {
            float sliceZ = (slice / Math.Max(1, (float)(_DistanceField.SliceCount)));
            return (sliceZ * _DistanceField.VirtualDepth) + _DistanceField.ZOffset;
        }

        private class BeginSliceBatchArgs {
            public AutoRenderTarget RenderTarget;
            public int SliceX, SliceY;
            public ViewTransform ViewTransform;

            internal static readonly LowAllocConcurrentQueue<BeginSliceBatchArgs> Pool =
                new LowAllocConcurrentQueue<BeginSliceBatchArgs>();
        }

        private void _BeginSliceBatch (DeviceManager dm, object userData) {
            var args = (BeginSliceBatchArgs)userData;
            var df = _DistanceField;
            var ddf = _DistanceField as DynamicDistanceField;

            // FIXME: dynamic/static split
            if (df.NeedClear) {
                df.NeedClear = false;
                dm.Device.Clear(ClearOptions.Target, DistanceField.GetClearValue(), 0, 0);
            }

            dm.AssertRenderTarget(args.RenderTarget.Get());

            // TODO: Optimize this
            dm.Device.ScissorRectangle = new Rectangle(
                args.SliceX, args.SliceY, df.SliceWidth, df.SliceHeight
            );

            var m = IlluminantMaterials.DistanceToPolygon;

            Materials.PushViewTransform(ref args.ViewTransform);
            SetDistanceFieldParameters(m, false, Configuration.DefaultQuality);

            foreach (var m2 in IlluminantMaterials.DistanceFunctionTypes)
                SetDistanceFieldParameters(m2, false, Configuration.DefaultQuality);
        }

        private void _EndSliceBatch (DeviceManager dm, object userData) {
            Materials.PopViewTransform();

            if (userData is BeginSliceBatchArgs args)
                BeginSliceBatchArgs.Pool.Enqueue(args);
        }

        private void RenderDistanceFieldSliceTriplet (
            AutoRenderTarget renderTarget, BatchGroup rtGroup, 
            int physicalSliceIndex, int firstVirtualSliceIndex, 
            ref int layer, bool? dynamicFlagFilter
        ) {
            var df = _DistanceField;
            var ddf = _DistanceField as DynamicDistanceField;

            var m = IlluminantMaterials.DistanceToPolygon;

            var sliceX = (physicalSliceIndex % df.ColumnCount) * df.SliceWidth;
            var sliceY = (physicalSliceIndex / df.ColumnCount) * df.SliceHeight;
            var sliceXVirtual = (physicalSliceIndex % df.ColumnCount) * df.VirtualWidth;
            var sliceYVirtual = (physicalSliceIndex / df.ColumnCount) * df.VirtualHeight;

            var viewTransform = ViewTransform.CreateOrthographic(
                0, 0, 
                df.VirtualWidth * df.ColumnCount, 
                df.VirtualHeight * df.RowCount
            );
            viewTransform.Position = new Vector2(-sliceXVirtual, -sliceYVirtual);

            var lastVirtualSliceIndex = firstVirtualSliceIndex + 2;
            if (!BeginSliceBatchArgs.Pool.TryDequeue(out var args))
                args = new BeginSliceBatchArgs();
            args.RenderTarget = renderTarget;
            args.ViewTransform = viewTransform;
            args.SliceX = sliceX;
            args.SliceY = sliceY;

            using (var group = BatchGroup.New(
                rtGroup, layer++,
                BeginSliceBatch, EndSliceBatch, args
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, -2, "LightingRenderer {0} : Begin Distance Field Slices [{1}-{2}]", this.ToObjectID(), firstVirtualSliceIndex, lastVirtualSliceIndex);

                ClearDistanceFieldSlice(
                    QuadIndices, group, -1, firstVirtualSliceIndex, dynamicFlagFilter == true ? ddf.StaticTexture.Get() : null
                );

                RenderDistanceFieldDistanceFunctions(firstVirtualSliceIndex, group, dynamicFlagFilter);
                RenderDistanceFieldHeightVolumes(firstVirtualSliceIndex, group, dynamicFlagFilter);

                if (OnRenderDistanceFieldSlice != null)
                    OnRenderDistanceFieldSlice(
                        this, firstVirtualSliceIndex, group, dynamicFlagFilter,
                        new Rectangle(sliceX, sliceY, df.SliceWidth, df.SliceHeight),
                        GetSliceVector(firstVirtualSliceIndex)
                    );

                // FIXME: Slow
                for (var i = firstVirtualSliceIndex; i <= lastVirtualSliceIndex; i++) {
                    if (ddf != null)
                        ddf.ValidateSlice(i, dynamicFlagFilter.Value);
                    else
                        df.ValidateSlice(i);
                }

                if (ddf != null)
                    ddf.MarkValidSlice(lastVirtualSliceIndex + 1, dynamicFlagFilter.Value);
                else
                    df.MarkValidSlice(lastVirtualSliceIndex + 1);

                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, 9999, "LightingRenderer {0} : End Distance Field Slices [{1}-{2}]", this.ToObjectID(), firstVirtualSliceIndex, lastVirtualSliceIndex);
            }
        }

        Action<DeviceManager, object> SetupDistanceFieldHeightVolume,
            SetupDistanceFieldHeightVolumeDraw,
            SetupDistanceFieldDistanceFunction;

        private void _SetupDistanceFieldDistanceFunction (DeviceManager dm, object userData) {
            var buffer = (DistanceFunctionBuffer)userData;
            dm.Device.RasterizerState = RenderStates.ScissorOnly;
            dm.Device.DepthStencilState = DepthStencilState.None;

            lock (buffer)
                buffer.Flush();
        }

        private void _SetupDistanceFieldHeightVolume (DeviceManager dm, object userData) {
            dm.Device.RasterizerState = RenderStates.ScissorOnly;
            dm.Device.DepthStencilState = DepthStencilState.None;
            SetDistanceFieldParameters(IlluminantMaterials.DistanceToPolygon, false, Configuration.DefaultQuality);
        }

        private void _SetupDistanceFieldHeightVolumeDraw (DeviceManager dm, object userData) {
            var cacheData = (HeightVolumeCacheData)userData;
            var hv = cacheData.Volume;
            var ep = IlluminantMaterials.DistanceToPolygon.Parameters;
            var pb = hv.Bounds;
            var tl = pb.TopLeft;
            var br = pb.BottomRight;
            ep["Bounds"].SetValue(new Vector4(tl.X, tl.Y, br.X, br.Y));
            ep["Uv"].SetValue(new Vector4(0, 0, 1.0f / hv.Polygon.Count, 0));
            ep["VertexDataTexture"].SetValue(cacheData.VertexDataTexture);
        }

        private Vector4 GetSliceVector (int firstVirtualSliceIndex) =>
            new Vector4(
                SliceIndexToZ(firstVirtualSliceIndex),
                SliceIndexToZ(firstVirtualSliceIndex + 1),
                SliceIndexToZ(firstVirtualSliceIndex + 2),
                SliceIndexToZ(firstVirtualSliceIndex + 3)
            );

        private void RenderDistanceFieldHeightVolumes (
            int firstVirtualIndex, BatchGroup group, bool? dynamicFlagFilter
        ) {
            if (Environment.HeightVolumes.Count <= 0)
                return;

            int i = 1;

            var mat = IlluminantMaterials.DistanceToPolygon;
            var sliceZ = GetSliceVector(firstVirtualIndex);

            if (SetupDistanceFieldHeightVolume == null)
                SetupDistanceFieldHeightVolume = _SetupDistanceFieldHeightVolume;
            if (SetupDistanceFieldHeightVolumeDraw == null)
                SetupDistanceFieldHeightVolumeDraw = _SetupDistanceFieldHeightVolumeDraw;

            // Rasterize the height volumes in sequential order.
            // FIXME: Depth buffer/stencil buffer tricks should work for generating this SDF, but don't?

            using (var innerGroup = BatchGroup.New(group, 2, SetupDistanceFieldHeightVolume))
            foreach (var hv in Environment.HeightVolumes) {
                if ((dynamicFlagFilter != null) && (hv.IsDynamic != dynamicFlagFilter.Value))
                    continue;

                var p = hv.Polygon;
                var m = hv.Mesh3D;
                var b = hv.Bounds.Expand(DistanceLimit, DistanceLimit);
                var zRange = new Vector2(hv.ZBase, hv.ZBase + hv.Height);

                HeightVolumeCacheData cacheData;

                // FIXME: Handle position/zrange updates
                lock (HeightVolumeCache)
                    HeightVolumeCache.TryGetValue(p, out cacheData);

                if (cacheData == null) {
                    cacheData = new HeightVolumeCacheData(hv);
                    
                    lock (Coordinator.CreateResourceLock)
                        cacheData.VertexDataTexture = new Texture2D(Coordinator.Device, p.Count, 1, false, SurfaceFormat.Vector4) {
                            Name = "LightingRenderer.HeightVolumeCacheData.VertexDataTexture",
                        };

                    lock (Coordinator.UseResourceLock)
                    using (var vertices = BufferPool<Vector4>.Allocate(p.Count)) {
                        for (var j = 0; j < p.Count; j++) {
                            var edgeA = p[j];
                            var edgeB = p[Arithmetic.Wrap(j + 1, 0, p.Count - 1)];
                            vertices.Data[j] = new Vector4(
                                edgeA.X, edgeA.Y, edgeB.X, edgeB.Y
                            );
                        }

                        cacheData.VertexDataTexture.SetData(vertices.Data, 0, p.Count);
                    }

                    lock (HeightVolumeCache)
                        HeightVolumeCache[p] = cacheData;
                }

                cacheData.BoundingBoxVertices[0] = new HeightVolumeVertex(new Vector3(b.TopLeft, 0), Vector3.Up, zRange, true);
                cacheData.BoundingBoxVertices[1] = new HeightVolumeVertex(new Vector3(b.TopRight, 0), Vector3.Up, zRange, true);
                cacheData.BoundingBoxVertices[2] = new HeightVolumeVertex(new Vector3(b.BottomRight, 0), Vector3.Up, zRange, true);
                cacheData.BoundingBoxVertices[3] = new HeightVolumeVertex(new Vector3(b.BottomLeft, 0), Vector3.Up, zRange, true);

                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    innerGroup, i, mat,
                    SetupDistanceFieldHeightVolumeDraw, cacheData
                )) {
                    batch.MaterialParameters.Add("SliceZ", sliceZ);
                    batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                        PrimitiveType.TriangleList,
                        cacheData.BoundingBoxVertices, 0, cacheData.BoundingBoxVertices.Length, QuadIndices, 0, 2
                    ));
                }

                i++;
            }
        }

        private void _BeginClearSliceBatch (DeviceManager dm, object userData) {
            var material = IlluminantMaterials.ClearDistanceFieldSlice;
            var clearTexture = (Texture2D)userData;
            material.Parameters["ClearTexture"].SetValue(clearTexture ?? _DummyDistanceFieldTexture);
        }

        // HACK: Make sure this is big enough to accommodate the maximum number of slices in a distance field
        private VertexPositionVector4[] ClearDistanceFieldSliceVertices = new VertexPositionVector4[1024];

        private float GetClearValueForSlice (float sliceZ) {
            if (!Environment.EnableGroundShadows)
                return DistanceField.GetClearValue().X;
            return sliceZ - Environment.GroundZ;
        }

        private Vector4 GetClearValue (Vector4 sliceZ) =>
            new Vector4(
                GetClearValueForSlice(sliceZ.X),
                GetClearValueForSlice(sliceZ.Y),
                GetClearValueForSlice(sliceZ.Z),
                GetClearValueForSlice(sliceZ.W)
            );

        private void ClearDistanceFieldSlice (
            short[] indices, IBatchContainer container, int layer, int firstSliceIndex, Texture2D clearTexture
        ) {
            // var color = new Color((firstSliceIndex * 16) % 255, 0, 0, 0);
            var sliceZValues = GetSliceVector(firstSliceIndex);
            var color = GetClearValue(sliceZValues);

            // We do one draw per group of 3 slices, so we need to make sure we don't reuse the same vertices for multiple slices
            // If we don't do this we get incorrect initial Z values for each slice and everything goes to hell
            int i = (firstSliceIndex / 3) * 4;
            ClearDistanceFieldSliceVertices[i + 0] = new VertexPositionVector4(new Vector3(0, 0, 0), color);
            ClearDistanceFieldSliceVertices[i + 1] = new VertexPositionVector4(new Vector3(_DistanceField.VirtualWidth, 0, 0), color);
            ClearDistanceFieldSliceVertices[i + 2] = new VertexPositionVector4(new Vector3(_DistanceField.VirtualWidth, _DistanceField.VirtualHeight, 0), color);
            ClearDistanceFieldSliceVertices[i + 3] = new VertexPositionVector4(new Vector3(0, _DistanceField.VirtualHeight, 0), color);

            using (var batch = PrimitiveBatch<VertexPositionVector4>.New(
                container, layer, IlluminantMaterials.ClearDistanceFieldSlice, BeginClearSliceBatch, clearTexture
            ))
                batch.Add(new PrimitiveDrawCall<VertexPositionVector4>(
                    PrimitiveType.TriangleList,
                    ClearDistanceFieldSliceVertices, i, 4, indices, 0, 2
                ));
        }

        private DistanceFunctionBuffer PickDistanceFunctionBuffer (bool? dynamicFlagFilter) {
            if (dynamicFlagFilter == false)
                return StaticDistanceFunctions;
            else
                return DynamicDistanceFunctions;
        }

        private void BuildDistanceFieldDistanceFunctionBuffer (bool? dynamicFlagFilter) {
            var result = PickDistanceFunctionBuffer(dynamicFlagFilter);
            var items = Environment.Obstructions;

            // HACK: Sort all the functions by type, fill the VB with each group,
            //  then issue a single draw for each
            using (var buffer = BufferPool<LightObstruction>.Allocate(items.Count))
            lock (result) {
                for (int i = 0; i < result.FirstOffset.Length; i++)
                    result.FirstOffset[i] = -1;
                Array.Clear(result.PrimCount, 0, result.PrimCount.Length);

                items.CopyTo(buffer.Data);
                Sort.FastCLRSortRef(new ArraySegment<LightObstruction>(buffer.Data), LightObstructionTypeComparer.Instance, 0, items.Count);
                
                result.IsDirty = true;
                result.EnsureSize(items.Count);

                int j = 0;
                for (int i = 0; i < items.Count; i++) {
                    var item = buffer.Data[i];
                    var type = (int)item.Type;

                    if ((dynamicFlagFilter != null) && (item.IsDynamic != dynamicFlagFilter.Value))
                        continue;

                    if (result.FirstOffset[type] == -1)
                        result.FirstOffset[type] = j;

                    result.PrimCount[type]++;

                    result.Vertices[j++] = item.Vertex;
                }

                result.EnsureVertexBuffer();
            }
        }

        private void RenderDistanceFieldDistanceFunctions (
            int firstVirtualIndex, BatchGroup group, bool? dynamicFlagFilter
        ) {
            var items = Environment.Obstructions;
            if (items.Count <= 0)
                return;

            int count = items.Count;

            var sliceZ = GetSliceVector(firstVirtualIndex);

            var numTypes = (int)LightObstruction.MAX_Type + 1;
            var batches  = new NativeBatch[numTypes];

            if (SetupDistanceFieldDistanceFunction == null)
                SetupDistanceFieldDistanceFunction = _SetupDistanceFieldDistanceFunction;

            for (int k = 0; k < 2; k++) {
                var dynamicFlag = (k != 0);
                if (dynamicFlagFilter.HasValue && dynamicFlagFilter.Value != dynamicFlag)
                    continue;

                var buffer = PickDistanceFunctionBuffer(dynamicFlag);
                lock (buffer)
                for (int i = 0; i < numTypes; i++) {
                    if (buffer.PrimCount[i] <= 0)
                        continue;

                    var m = IlluminantMaterials.DistanceFunctionTypes[i];
                    if (RenderTrace.EnableTracing)
                        RenderTrace.Marker(group, (i * 2) + 3, "LightingRenderer {0} : Render {1}(s) to {2} buffer", this.ToObjectID(), (LightObstructionType)i, (buffer == DynamicDistanceFunctions) ? "dynamic" : "static");

                    using (var batch = NativeBatch.New(
                        group, (i * 2) + 4, m, SetupDistanceFieldDistanceFunction, userData: buffer
                    )) {
                        batch.MaterialParameters.Add("SliceZ", sliceZ);
                        batch.Add(new NativeDrawCall(
                            PrimitiveType.TriangleList,
                            CornerBuffer, 0, 
                            buffer.VertexBuffer, buffer.FirstOffset[i], 
                            null, 0,
                            QuadIndexBuffer, 0, 0, 
                            4, 0, 
                            2, buffer.PrimCount[i]
                        ));
                    }
                }
            }
        }

        private Action<DeviceManager, object> SetupDistanceFieldPartition, TeardownDistanceFieldPartition;

        // FIXME: Can we use a view transform modifier instead of these callbacks?
        private void _SetupDistanceFieldPartition (DeviceManager dm, object userData) {
            var vt = Materials.ViewTransform;
            vt.ResetZRanges();
            Materials.PushViewTransform(ref vt);
        }

        private void _TeardownDistanceFieldPartition (DeviceManager dm, object userData) {
            Materials.PopViewTransform();
        }

        private void RenderDistanceFieldPartition (ref int layerIndex, IBatchContainer resultGroup, bool? dynamicFlagFilter) {
            var ddf = _DistanceField as DynamicDistanceField;
            if (ddf == null)
                dynamicFlagFilter = null;

            var isRenderingStatic = ((ddf != null) && (dynamicFlagFilter == false));
            var sliceInfo = isRenderingStatic ? ddf.StaticSliceInfo : _DistanceField.SliceInfo;
            var renderTarget = isRenderingStatic ? ddf.StaticTexture : _DistanceField.Texture;

            int sliceCount = _DistanceField.SliceCount;
            int slicesToUpdate =
                Math.Min(
                    Configuration.MaximumFieldUpdatesPerFrame,
                    // FIXME
                    sliceInfo.InvalidSlices.Count
                );
            if (slicesToUpdate <= 0)
                return;

            if (SetupDistanceFieldPartition == null)
                SetupDistanceFieldPartition = _SetupDistanceFieldPartition;
            if (TeardownDistanceFieldPartition == null)
                TeardownDistanceFieldPartition = _TeardownDistanceFieldPartition;

            using (var rtGroup = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex++, renderTarget,
                // HACK: Since we're mucking with view transforms, do a save and restore
                SetupDistanceFieldPartition,
                TeardownDistanceFieldPartition,
                name: "Render Distance Field Partition"
            )) {
                // We incrementally do a partial update of the distance field.
                int layer = 0;

                BuildDistanceFieldDistanceFunctionBuffer(dynamicFlagFilter);

                while (slicesToUpdate > 0) {
                    // FIXME
                    var slice = sliceInfo.InvalidSlices[0];
                    var physicalSlice = slice / PackedSliceCount;

                    RenderDistanceFieldSliceTriplet(
                        renderTarget, rtGroup, 
                        physicalSlice, slice, ref layer, dynamicFlagFilter
                    );

                    slicesToUpdate -= 3;
                }
            }
        }
    }
}
