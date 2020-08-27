using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Tracing;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
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
            return sliceZ * Environment.MaximumZ;
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

            Action<DeviceManager, object> beginSliceBatch =
                (dm, _) => {
                    // FIXME: dynamic/static split
                    if (df.NeedClear) {
                        df.NeedClear = false;
                        dm.Device.Clear(Color.Transparent);
                    }

                    dm.AssertRenderTarget(renderTarget.Get());

                    // TODO: Optimize this
                    dm.Device.ScissorRectangle = new Rectangle(
                        sliceX, sliceY, df.SliceWidth, df.SliceHeight
                    );

                    Materials.ApplyViewTransformToMaterial(IlluminantMaterials.ClearDistanceFieldSlice, ref viewTransform);
                    Materials.ApplyViewTransformToMaterial(m, ref viewTransform);
                    SetDistanceFieldParameters(m, false, Configuration.DefaultQuality);

                    foreach (var m2 in IlluminantMaterials.DistanceFunctionTypes) {
                        Materials.ApplyViewTransformToMaterial(m2, ref viewTransform);
                        SetDistanceFieldParameters(m2, false, Configuration.DefaultQuality);
                    }
                };

            var lastVirtualSliceIndex = firstVirtualSliceIndex + 2;

            using (var group = BatchGroup.New(rtGroup, layer++,
                beginSliceBatch, null
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, -2, "LightingRenderer {0} : Begin Distance Field Slices [{1}-{2}]", this.ToObjectID(), firstVirtualSliceIndex, lastVirtualSliceIndex);

                ClearDistanceFieldSlice(
                    QuadIndices, group, -1, firstVirtualSliceIndex, dynamicFlagFilter == true ? ddf.StaticTexture.Get() : null
                );

                RenderDistanceFieldDistanceFunctions(firstVirtualSliceIndex, group, dynamicFlagFilter);
                RenderDistanceFieldHeightVolumes(firstVirtualSliceIndex, group, dynamicFlagFilter);

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

        private void RenderDistanceFieldHeightVolumes (
            int firstVirtualIndex, BatchGroup group, bool? dynamicFlagFilter
        ) {
            if (Environment.HeightVolumes.Count <= 0)
                return;

            int i = 1;

            var mat = IlluminantMaterials.DistanceToPolygon;
            var sliceZ = new Vector4(
                SliceIndexToZ(firstVirtualIndex),
                SliceIndexToZ(firstVirtualIndex + 1),
                SliceIndexToZ(firstVirtualIndex + 2),
                SliceIndexToZ(firstVirtualIndex + 3)
            );

            // Rasterize the height volumes in sequential order.
            // FIXME: Depth buffer/stencil buffer tricks should work for generating this SDF, but don't?
            using (var innerGroup = BatchGroup.New(group, 2, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(mat, false, Configuration.DefaultQuality);
            }))
            foreach (var hv in Environment.HeightVolumes) {
                if ((dynamicFlagFilter != null) && (hv.IsDynamic != dynamicFlagFilter.Value))
                    continue;

                var p = hv.Polygon;
                var m = hv.Mesh3D;
                var pb = p.Bounds;
                var b = hv.Bounds.Expand(DistanceLimit, DistanceLimit);
                var zRange = new Vector2(hv.ZBase, hv.ZBase + hv.Height);

                HeightVolumeCacheData cacheData;

                // FIXME: Handle position/zrange updates
                lock (HeightVolumeCache)
                    HeightVolumeCache.TryGetValue(p, out cacheData);

                if (cacheData == null) {
                    cacheData = new HeightVolumeCacheData();
                    
                    lock (Coordinator.CreateResourceLock)
                        cacheData.VertexDataTexture = new Texture2D(Coordinator.Device, p.Count, 1, false, SurfaceFormat.Vector4);

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
                    (dm, _) => {
                        var ep = mat.Effect.Parameters;
                        var tl = pb.TopLeft;
                        var br = pb.BottomRight;
                        ep["Bounds"].SetValue(new Vector4(tl.X, tl.Y, br.X, br.Y));
                        ep["Uv"].SetValue(new Vector4(0, 0, 1.0f / p.Count, 0));
                        ep["VertexDataTexture"].SetValue(cacheData.VertexDataTexture);
                        ep["SliceZ"].SetValue(sliceZ);
                    }
                ))
                    batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                        PrimitiveType.TriangleList,
                        cacheData.BoundingBoxVertices, 0, cacheData.BoundingBoxVertices.Length, QuadIndices, 0, 2
                    ));

                i++;
            }
        }

        private void ClearDistanceFieldSlice (
            short[] indices, IBatchContainer container, int layer, int firstSliceIndex, Texture2D clearTexture
        ) {
            // var color = new Color((firstSliceIndex * 16) % 255, 0, 0, 0);
            var color = Color.Transparent;

            var verts = new VertexPositionColor[] {
                new VertexPositionColor(new Vector3(0, 0, 0), color),
                new VertexPositionColor(new Vector3(_DistanceField.VirtualWidth, 0, 0), color),
                new VertexPositionColor(new Vector3(_DistanceField.VirtualWidth, _DistanceField.VirtualHeight, 0), color),
                new VertexPositionColor(new Vector3(0, _DistanceField.VirtualHeight, 0), color)
            };

            var material = IlluminantMaterials.ClearDistanceFieldSlice;
            using (var batch = PrimitiveBatch<VertexPositionColor>.New(
                container, layer, material,
                (dm, _) => {
                    material.Effect.Parameters["ClearTexture"].SetValue(clearTexture ?? _DummyDistanceFieldTexture);
                    material.Effect.Parameters["ClearMultiplier"].SetValue(clearTexture != null ? Vector4.One : Vector4.Zero);
                    material.Effect.Parameters["ClearInverseScale"].SetValue(new Vector2(
                        1.0f / (_DistanceField.SliceWidth * _DistanceField.ColumnCount), 
                        1.0f / (_DistanceField.SliceHeight * _DistanceField.RowCount)
                    ));
                }
            ))
                batch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                    PrimitiveType.TriangleList,
                    verts, 0, 4, indices, 0, 2
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
                Sort.FastCLRSortRef(buffer.Data, LightObstructionTypeComparer.Instance, 0, items.Count);
                
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

                    result.Vertices[j++] = new DistanceFunctionVertex(item.Center, item.Size, item.Rotation, item.Type);
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

            var sliceZ = new Vector4(
                SliceIndexToZ(firstVirtualIndex),
                SliceIndexToZ(firstVirtualIndex + 1),
                SliceIndexToZ(firstVirtualIndex + 2),
                SliceIndexToZ(firstVirtualIndex + 3)
            );

            var numTypes = (int)LightObstruction.MAX_Type + 1;
            var batches  = new NativeBatch[numTypes];

            Action<DeviceManager, object> setup = null;

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

                    setup = (dm, _) => {
                        dm.Device.RasterizerState = RenderStates.ScissorOnly;
                        dm.Device.DepthStencilState = DepthStencilState.None;
                        m.Effect.Parameters["SliceZ"].SetValue(sliceZ);

                        lock (buffer)
                            buffer.Flush();
                    };

                    using (var batch = NativeBatch.New(
                        group, (i * 2) + 4, m, setup
                    )) {
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

            using (var rtGroup = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex++, renderTarget,
                // HACK: Since we're mucking with view transforms, do a save and restore
                (dm, _) => {
                    var vt = Materials.ViewTransform;
                    vt.ResetZRanges();
                    Materials.PushViewTransform(ref vt);
                },
                (dm, _) => {
                    Materials.PopViewTransform();
                },
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
