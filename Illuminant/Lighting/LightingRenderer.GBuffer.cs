﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Tracing;
using Squared.Util;

namespace Squared.Illuminant {
    public delegate void UserGBufferRenderer (LightingRenderer lightingRenderer, ref ImperativeRenderer billboardRenderer);
    public delegate void UserDistanceFieldRenderer (
        LightingRenderer lightingRenderer, int firstVirtualSliceIndex, BatchGroup group, bool? dynamicFlagFilter, 
        Rectangle sliceRect, Vector4 sliceZValues
    );

    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        public event UserGBufferRenderer OnRenderGBuffer;
        public event UserDistanceFieldRenderer OnRenderDistanceFieldSlice;

        private GBufferTransformArguments PendingGBufferArguments = new GBufferTransformArguments();
        private Action<DeviceManager, object> SetupGBufferGroundPlane;
        private object BoxedRenderScale = default(Vector2);
        private short[] BillboardQuadIndices;
        private MaterialParameterValues.Storage GBufferParameterStorage;
        private Action<DeviceManager, object> UpdateScaleFactorForGBufferBitmapMaterial;
        private HeightVolumeVertex[] GroundPlaneVerts = new HeightVolumeVertex[4];

        public GBuffer GBuffer {
            get {
                return _GBuffer;
            }
        }

        private void EnsureGBuffer (int width, int height) {
            if (Configuration.EnableGBuffer && (width > 0) && (height > 0)) {
                if ((_GBuffer != null) && ((_GBuffer.Width != width) || (_GBuffer.Height != height))) {
                    Coordinator.DisposeResource(_GBuffer);
                    _GBuffer = null;
                }

                if (_GBuffer == null) {
                    _GBuffer = new GBuffer(
                        Coordinator, width, height,
                        Configuration.HighQuality
                    );
                    if (!string.IsNullOrWhiteSpace(_Name))
                        _GBuffer.Texture.SetName(_Name);
                }
            } else {
                if (_GBuffer != null) {
                    Coordinator.DisposeResource(_GBuffer);
                    _GBuffer = null;
                }
            }
        }

        private float ComputeSelfOcclusionHack () {
            if (_DistanceField == null) {
                return 0;
            } else {
                var ratioBias = Math.Max((1.0 / _DistanceField.Resolution) - 1, 0);
                var scaledRatioBias = Math.Pow(ratioBias, 1.5);
                var result = (float)(0.5 + (scaledRatioBias * 0.05));
                return result;
            }
        }

        private float ComputeZSelfOcclusionHack () {
            if (_DistanceField == null) {
                return 0;
            } else {
                float sliceSize = _DistanceField.VirtualDepth / _DistanceField.SliceCount;
                return Math.Max(sliceSize * 0.525f, 1f);
            }
        }

        private class GBufferTransformArguments {
            public ViewTransform Transform;
        }

        private void _BeforeRenderGBuffer (DeviceManager dm, object userData) {
            dm.PushStates();
            Materials.PushViewTransform(ref PendingGBufferArguments.Transform);
            dm.AssertRenderTarget(_GBuffer.Texture.Get());
        }

        private void _UpdateScaleFactorForGBufferBitmapMaterial (DeviceManager dm, object userData) {
            var scaleFactor = (Vector2)userData;
            var invScaleFactor = new Vector2(1.0f / scaleFactor.X, 1.0f / scaleFactor.Y);
            IlluminantMaterials.AutoGBufferBitmap.Parameters["ViewCoordinateScaleFactor"].SetValue(invScaleFactor);
        }

        private void _AfterRenderGBuffer (DeviceManager dm, object userData) {
            Materials.PopViewTransform();
            dm.PopStates();
        }

        private void _SetupGBufferGroundPlane (DeviceManager dm, object userData) {
            var sohack = ComputeSelfOcclusionHack();
            var zsohack = ComputeZSelfOcclusionHack();
            var p = IlluminantMaterials.HeightVolumeFace.Parameters;
            p["DistanceFieldExtent"].SetValue(Extent3);
            p["SelfOcclusionHack"].SetValue(sohack);
            p["ZSelfOcclusionHack"].SetValue(zsohack);
            EnvironmentUniforms.SetIntoParameters(p);

            p = IlluminantMaterials.GroundPlane.Parameters;
            p["DistanceFieldExtent"].SetValue(Extent3);
            p["SelfOcclusionHack"].SetValue(sohack);
            p["ZSelfOcclusionHack"].SetValue(zsohack);
            EnvironmentUniforms.SetIntoParameters(p);

            p = IlluminantMaterials.HeightVolume.Parameters;
            p["DistanceFieldExtent"].SetValue(Extent3);
            p["SelfOcclusionHack"].SetValue(sohack);
            p["ZSelfOcclusionHack"].SetValue(zsohack);
            EnvironmentUniforms.SetIntoParameters(p);

            dm.Device.RasterizerState = RenderStates.ScissorOnly;
        }

        private void RenderGBuffer (
            ref int layerIndex, IBatchContainer resultGroup, Vector2? viewportScale,
            bool enableHeightVolumes = true, bool enableBillboards = true
        ) {
            var actualScaleFactor = viewportScale.GetValueOrDefault(Vector2.One) * Configuration.RenderScale;
            CreateViewTransform(_GBuffer.Width, _GBuffer.Height, out var vt);
            vt.Position = PendingFieldViewportPosition.GetValueOrDefault(Vector2.Zero);
            vt.Scale = actualScaleFactor;
            PendingGBufferArguments.Transform = vt;

            // FIXME: Is this right?
            using (var group = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex, _GBuffer.Texture,
                // FIXME: Optimize this
                BeforeRenderGBuffer, AfterRenderGBuffer,
                name: "Render G-Buffer"
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, -1, "LightingRenderer {0} : Begin G-Buffer", this.ToObjectID());

                ClearBatch.AddNew(
                    group, 0, Materials.Clear, 
                    Color.Transparent, clearZ: 0
                );

                if (SetupGBufferGroundPlane == null)
                    SetupGBufferGroundPlane = _SetupGBufferGroundPlane;

                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 1, IlluminantMaterials.GroundPlane,
                    SetupGBufferGroundPlane
                )) {
                    RenderGroundPlane(batch);

                    if (Configuration.TwoPointFiveD) {
                        RenderTwoPointFiveDVolumes(enableHeightVolumes, enableBillboards, group);
                    } else if (enableHeightVolumes) {
                        // fixme: needs self occlusion hack
                        RenderGBufferVolumes(batch);
                        RenderGBufferBillboards(group, 10);
                    } else
                        RenderGBufferBillboards(group, 10);
                }

                // TODO: Update the heightmap using any SDF light obstructions (maybe only if they're flagged?)

                if (OnRenderGBuffer != null) {

                    if (UpdateScaleFactorForGBufferBitmapMaterial == null)
                        UpdateScaleFactorForGBufferBitmapMaterial = _UpdateScaleFactorForGBufferBitmapMaterial;
                    if ((Vector2)BoxedRenderScale != Configuration.RenderScale) 
                        BoxedRenderScale = Configuration.RenderScale;

                    using (var userContentGroup = BatchGroup.New(group, 100, UpdateScaleFactorForGBufferBitmapMaterial, userData: BoxedRenderScale)) {
                        if (RenderTrace.EnableTracing)
                            RenderTrace.Marker(userContentGroup, -999, "LightingRenderer {0} : Begin User G-Buffer Content", this.ToObjectID());

                        var ir = new ImperativeRenderer(
                            userContentGroup, Materials,
                            layer: 0,
                            depthStencilState: FrontFaceDepthStencilState,
                            blendState: BlendState.Opaque,
                            flags: ImperativeRendererFlags.UseZBuffer | ImperativeRendererFlags.UseDiscard
                        ) {
                            DefaultBitmapMaterial = IlluminantMaterials.AutoGBufferBitmap
                        };
                        GBufferParameterStorage = GBufferParameterStorage.EnsureUniqueStorage(ref ir.Parameters);
                        ir.Parameters.UseExistingListStorage(GBufferParameterStorage, false);

                        OnRenderGBuffer(this, ref ir);
                    }
                }

                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, 9999, "LightingRenderer {0} : End G-Buffer", this.ToObjectID());
            }
        }

        private void RenderGBufferVolumes (PrimitiveBatch<HeightVolumeVertex> batch) {
            if (Environment.HeightVolumes.Count == 0)
                return;

            // Rasterize the height volumes in order from lowest to highest.
            foreach (var hv in Environment.HeightVolumes.OrderBy(hv => hv.ZBase + hv.Height)) {
                var b = hv.Bounds;
                var m = hv.Mesh3D;

                batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                    PrimitiveType.TriangleList,
                    m, 0, m.Length / 3
                ));
            }
        }

        private void RenderTwoPointFiveDVolumes (bool enableHeightVolumes, bool enableBillboards, BatchGroup group) {
            if (RenderTrace.EnableTracing) {
                if (enableHeightVolumes) {
                    RenderTrace.Marker(group, 2, "LightingRenderer {0} : G-Buffer Top Faces", this.ToObjectID());
                    RenderTrace.Marker(group, 4, "LightingRenderer {0} : G-Buffer Front Faces", this.ToObjectID());
                }

                RenderTrace.Marker(group, 6, "LightingRenderer {0} : G-Buffer Billboards", this.ToObjectID());
            }

            if (enableHeightVolumes && Environment.HeightVolumes.Count > 0)
                using (var topBatch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 3, Materials.Get(
                        IlluminantMaterials.HeightVolume,
                        depthStencilState: TopFaceDepthStencilState,
                        rasterizerState: Render.Convenience.RenderStates.ScissorOnly,
                        blendState: BlendState.Opaque
                    )
                ))
                using (var frontBatch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 5, Materials.Get(
                        IlluminantMaterials.HeightVolumeFace,
                        depthStencilState: FrontFaceDepthStencilState,
                        rasterizerState: Render.Convenience.RenderStates.ScissorOnly,
                        blendState: BlendState.Opaque
                    )
                )) {
                    foreach (var volume in Environment.HeightVolumes.OrderByDescending(hv => hv.ZBase + hv.Height)) {
                        var ffm3d = volume.GetFrontFaceMesh3D();
                        if (ffm3d.Count <= 0)
                            continue;

                        var m3d = volume.Mesh3D;

                        frontBatch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                            PrimitiveType.TriangleList,
                            ffm3d.Array, ffm3d.Offset, ffm3d.Count / 3
                        ));

                        topBatch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                            PrimitiveType.TriangleList,
                            m3d, 0, m3d.Length / 3
                        ));
                    }
                }

            if (enableBillboards)
                RenderGBufferBillboards(group, 7);
        }

        private void RenderGroundPlane (PrimitiveBatch<HeightVolumeVertex> batch) {
            // HACK: Fill in the gbuffer values for the ground plane

            var zRange = new Vector2(Environment.GroundZ, Environment.GroundZ);
            var tl = new Vector3(-999999, -999999, Environment.GroundZ);
            var tr = new Vector3(999999, -999999, Environment.GroundZ);
            var br = new Vector3(999999, 999999, Environment.GroundZ);
            var bl = new Vector3(-999999, 999999, Environment.GroundZ);

            if (Configuration.RenderGroundPlane == false) {
                var huge = new Vector3(0, 0, 99999);
                tl += huge;
                tr += huge;
                br += huge;
                bl += huge;
            }

            // FIXME: Potential race condition
            GroundPlaneVerts[0] = new HeightVolumeVertex(tl, Vector3.UnitZ, zRange, Environment.EnableGroundShadows);
            GroundPlaneVerts[1] = new HeightVolumeVertex(tr, Vector3.UnitZ, zRange, Environment.EnableGroundShadows);
            GroundPlaneVerts[2] = new HeightVolumeVertex(br, Vector3.UnitZ, zRange, Environment.EnableGroundShadows);
            GroundPlaneVerts[3] = new HeightVolumeVertex(bl, Vector3.UnitZ, zRange, Environment.EnableGroundShadows);

            batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                PrimitiveType.TriangleList, GroundPlaneVerts, 0, 4, QuadIndices, 0, 2
            ));
        }

        private void _SetTextureForGBufferBillboard (DeviceManager dm, ref PrimitiveDrawCall<BillboardVertex> drawCall, int index) {
            var material = dm.CurrentMaterial;
            material.Parameters["Mask"].SetValue((Texture)drawCall.UserData);
            material.Flush(dm);
            // HACK: Filtering causes artifacts so we're disabling it for now
            dm.Device.SamplerStates[0] = SamplerState.PointClamp;
        }

        private sealed class GBufferBillboardSorter : IRefComparer<Billboard> {
            public static readonly GBufferBillboardSorter Instance = new GBufferBillboardSorter();

            public int Compare (ref Billboard lhs, ref Billboard rhs) {
                int result = lhs.SortKey.CompareTo(rhs.SortKey);
                if (result == 0)
                    result = ((int)lhs.Type).CompareTo((int)rhs.Type);
                if (result == 0) {
                    var lhsTexId = lhs.Texture != null ? lhs.Texture.GetHashCode() : 0;
                    var rhsTexId = rhs.Texture != null ? rhs.Texture.GetHashCode() : 0;
                    result = lhsTexId.CompareTo(rhsTexId);
                }
                return result;
            }
        }

        private void _GBufferBillboardBatchSetup (DeviceManager dm, object userData) {
            var m = (Material)userData;
            var p = m.Parameters;
            p["DistanceFieldExtent"].SetValue(Extent3);
            p["SelfOcclusionHack"].SetValue(ComputeSelfOcclusionHack());
            EnvironmentUniforms.SetIntoParameters(p);
        }

        private void RenderGBufferBillboards (IBatchContainer container, int layerIndex) {
            if (Environment.Billboards == null)
                return;

            // FIXME: This suuuuuuuuuuucks
            BillboardScratch.UnsafeFastClear();
            BillboardScratch.AddRange(Environment.Billboards);
            BillboardScratch.FastCLRSortRef(GBufferBillboardSorter.Instance);

            if (BillboardScratch.Count <= 0)
                return;

            BillboardVertexScratch.EnsureCapacity(BillboardScratch.Count * 4);
            BillboardVertexScratch.UnsafeFastClear();

            var verts = BillboardVertexScratch.GetBufferArray();

            Texture2D previousTexture = null;
            BillboardType previousType = (BillboardType)(int)-99;
            int runStartedAt = 0, runStartedAtVertex = 0;
            int i, j;

            var requiredQuadIndices = ((BillboardScratch.Count * 6) + 63) / 64 * 64;
            if ((BillboardQuadIndices == null) || (BillboardQuadIndices.Length < requiredQuadIndices)) {
                BillboardQuadIndices = new short[requiredQuadIndices];
                int v = 0;
                for (i = 0; i < BillboardScratch.Count; i++) {
                    var o = i * 4;

                    for (j = 0; j < QuadIndices.Length; j++)
                        BillboardQuadIndices[v++] = (short)(QuadIndices[j] + o);
                }
            }

            i = 0;
            j = 0;
            using (var maskBatch = PrimitiveBatch<BillboardVertex>.New(
                container, layerIndex++, Materials.Get(
                    IlluminantMaterials.MaskBillboard,
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RenderStates.ScissorOnly,
                    blendState: BlendState.Opaque
                ),
                batchSetup: GBufferBillboardBatchSetup,
                userData: IlluminantMaterials.MaskBillboard
            )) 
            using (var gDataBatch = PrimitiveBatch<BillboardVertex>.New(
                container, layerIndex++, Materials.Get(
                    IlluminantMaterials.GDataBillboard,
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RenderStates.ScissorOnly,
                    blendState: BlendState.Opaque
                ), 
                batchSetup: GBufferBillboardBatchSetup,
                userData: IlluminantMaterials.GDataBillboard
            )) {
                void flushBatch (
                    int i, int runStartedAt,
                    BillboardType previousType, 
                    PrimitiveBatch<BillboardVertex> gDataBatch, 
                    PrimitiveBatch<BillboardVertex> maskBatch,
                    BillboardVertex[] verts,
                    int runStartedAtVertex,
                    Texture2D previousTexture
                ) {
                    var runLength = i - runStartedAt;
                    if (runLength <= 0)
                        return;

                    var batch = previousType == BillboardType.GBufferData
                        ? gDataBatch
                        : maskBatch;
                
                    batch.Add(new PrimitiveDrawCall<BillboardVertex>(
                        PrimitiveType.TriangleList, verts, runStartedAtVertex, runLength * 4,
                        BillboardQuadIndices, 0, runLength * 2,
                        sortKey: new DrawCallSortKey(order: i),
                        beforeDraw: SetTextureForGBufferBillboard, userData: previousTexture
                    ));
                }

                foreach (var billboard in BillboardScratch) {
                    if ((previousTexture != billboard.Texture) || (previousType != billboard.Type)) {
                        flushBatch(i, runStartedAt, previousType, gDataBatch, maskBatch, verts, runStartedAtVertex, previousTexture);
                        runStartedAt = i;
                        runStartedAtVertex = j;
                        previousTexture = billboard.Texture;
                        previousType = billboard.Type;
                    }

                    var sb = billboard.ScreenBounds.GetValueOrDefault();
                    var normal1 = billboard.Normal;
                    var normal2 = normal1;
                    var dataScaleAndDynamicFlag = new Vector2(
                        billboard.DataScale.GetValueOrDefault(1),
                        billboard.StaticLightingOnly ? -1 : 1
                    );

                    Bounds3 wb;
                    if (billboard.WorldBounds.HasValue)
                        wb = billboard.WorldBounds.Value;
                    else {
                        float baseZ = Environment.GroundZ * 1;
                        float x1 = sb.TopLeft.X, x2 = sb.BottomRight.X, y = sb.BottomRight.Y, h = billboard.WorldElevation.GetValueOrDefault(sb.Size.Y);
                        float zScale = (Environment.ZToYMultiplier > 0) ? h / Environment.ZToYMultiplier : 0;

                        if (billboard.Type == BillboardType.GBufferData) {
                            baseZ = billboard.WorldElevation.GetValueOrDefault(0);
                            zScale = 0;
                        }

                        wb = new Bounds3 {
                            Minimum = new Vector3(x1, y, baseZ + zScale),
                            Maximum = new Vector3(x2, y, baseZ)
                        };
                    }

                    if (!billboard.ScreenBounds.HasValue && billboard.WorldBounds.HasValue) {
                        sb = new Bounds(
                            new Vector2(wb.Minimum.X, wb.Minimum.Y - (wb.Minimum.Z * Environment.ZToYMultiplier)),
                            new Vector2(wb.Maximum.X, wb.Maximum.Y - (wb.Maximum.Z * Environment.ZToYMultiplier))
                        );

                        var minZ = Math.Min(wb.Minimum.Z, wb.Maximum.Z);
                        var maxZ = Math.Max(wb.Minimum.Z, wb.Maximum.Z);
                        // FIXME
                        // HACK: Paint the maximum Z value of the volume
                        wb.Minimum.Z = maxZ;
                        wb.Maximum.Z = maxZ;
                    }

                    wb.Minimum += billboard.WorldOffset;
                    wb.Maximum += billboard.WorldOffset;

                    // FIXME: Linear filtering = not a cylinder?
                    if (Math.Abs(billboard.CylinderFactor) >= 0.001f) {
                        normal1.X = 0f - (0.9f * billboard.CylinderFactor);
                        normal2.X = 0f + (0.9f * billboard.CylinderFactor);
                    }

                    var textureBounds = billboard.TextureBounds;
                    verts[j++] = new BillboardVertex {
                        ScreenPosition = sb.TopLeft,
                        Normal = normal1,
                        WorldPosition = wb.Minimum,
                        TexCoord = textureBounds.TopLeft,
                        DataScaleAndDynamicFlag = dataScaleAndDynamicFlag,
                    };
                    verts[j++] = new BillboardVertex {
                        ScreenPosition = sb.TopRight,
                        Normal = normal2,
                        WorldPosition = new Vector3(wb.Maximum.X, wb.Minimum.Y, wb.Minimum.Z),
                        TexCoord = textureBounds.TopRight,
                        DataScaleAndDynamicFlag = dataScaleAndDynamicFlag,
                    };
                    verts[j++] = new BillboardVertex {
                        ScreenPosition = sb.BottomRight,
                        Normal = normal2,
                        WorldPosition = wb.Maximum,
                        TexCoord = textureBounds.BottomRight,
                        DataScaleAndDynamicFlag = dataScaleAndDynamicFlag,
                    };
                    verts[j++] = new BillboardVertex {
                        ScreenPosition = sb.BottomLeft,
                        Normal = normal1,
                        WorldPosition = new Vector3(wb.Minimum.X, wb.Maximum.Y, wb.Maximum.Z),
                        TexCoord = textureBounds.BottomLeft,
                        DataScaleAndDynamicFlag = dataScaleAndDynamicFlag,
                    };

                    i++;
                }

                flushBatch(i, runStartedAt, previousType, gDataBatch, maskBatch, verts, runStartedAtVertex, previousTexture);
            }
        }

        private float ViewportScaleX () {
            return PendingDrawViewportScale?.X 
                ?? PendingFieldViewportScale?.X 
                ?? 1;
        }

        private float ViewportScaleY () {
            return PendingDrawViewportScale?.Y 
                ?? PendingFieldViewportScale?.Y 
                ?? 1;
        }

        private void SetGBufferParameters (MaterialEffectParameters p) {
            // FIXME: RenderScale?
            if (_GBuffer != null) {
                p["GBufferViewportRelative"].SetValue(Configuration.GBufferViewportRelative ? 1f : 0f);
                p["GBufferTexelSizeAndMisc"].SetValue(new Vector4(
                    _GBuffer.InverseSize, ViewportScaleX(), ViewportScaleY()
                ));
                p["GBuffer"].SetValue(_GBuffer.Texture.Get());
            } else {
                p["GBuffer"].SetValue(_DummyGBufferTexture);
                p["GBufferTexelSizeAndMisc"].SetValue(new Vector4(
                    0, 0, ViewportScaleX(), ViewportScaleY()
                ));
            }
        }

        private static readonly DepthStencilState MaskDepthStencilState = new DepthStencilState {
            ReferenceStencil = 1,
            StencilEnable = true,
            StencilFail = StencilOperation.Zero,
            StencilPass = StencilOperation.Replace,
            StencilFunction = CompareFunction.Always
        };

        private void UpdateMaskFromGBuffer (IBatchContainer container, int layer) {
            var material = Materials.Get(
                IlluminantMaterials.GBufferMask,
                depthStencilState: MaskDepthStencilState,
                blendState: RenderStates.DrawNone
            );
            using (var batch = PrimitiveBatch<VertexPositionTexture>.New(
                container, layer, material,
                (dm, _) => dm.Device.Textures[0] = GBuffer.Texture.Get()
            )) {
                var verts = new[] {
                    new VertexPositionTexture(new Vector3(-1, -1, Environment.GroundZ), Vector2.Zero),
                    new VertexPositionTexture(new Vector3(1, -1, Environment.GroundZ), new Vector2(1, 0)),
                    new VertexPositionTexture(new Vector3(1, 1, Environment.GroundZ), Vector2.One),
                    new VertexPositionTexture(new Vector3(-1, -1, Environment.GroundZ), Vector2.Zero),
                    new VertexPositionTexture(new Vector3(1, 1, Environment.GroundZ), Vector2.One),
                    new VertexPositionTexture(new Vector3(-1, 1, Environment.GroundZ), new Vector2(0, 1))
                };
                batch.Add(new PrimitiveDrawCall<VertexPositionTexture>(
                    PrimitiveType.TriangleList, verts, 0, 2
                ));
            }
        }
    }
}
