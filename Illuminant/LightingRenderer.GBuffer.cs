using System;
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
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        public GBuffer GBuffer {
            get {
                return _GBuffer;
            }
        }

        private void EnsureGBuffer () {
            if (Configuration.EnableGBuffer) {
                if (_GBuffer == null) {
                    _GBuffer = new GBuffer(
                        Coordinator, 
                        Configuration.MaximumRenderSize.First, 
                        Configuration.MaximumRenderSize.Second,
                        Configuration.HighQuality
                    );
                    _GBuffer.Texture.SetName(_Name);
                }
            } else {
                if (_GBuffer != null) {
                    Coordinator.DisposeResource(_GBuffer);
                    _GBuffer = null;
                }
            }
        }

        private float ComputeTopSelfOcclusionHack () {
            if (_DistanceField == null) {
                return 0;
            } else {
                var sliceSize = (Environment.MaximumZ / _DistanceField.SliceCount);
                var result = 0.5f + (sliceSize / 2);
                return result;
            }
        }

        private float ComputeFrontSelfOcclusionHack () {
            if (_DistanceField == null) {
                return 0;
            } else {
                var ratioBias = Math.Max((1.0 / _DistanceField.Resolution) - 1, 0);
                var scaledRatioBias = Math.Pow(ratioBias, 1.5);
                var result = (float)(0.5 + (scaledRatioBias * 0.05));
                return result;
            }
        }

        private void RenderGBuffer (
            ref int layerIndex, IBatchContainer resultGroup,
            int renderWidth, int renderHeight,
            bool enableHeightVolumes = true, bool enableBillboards = true
        ) {
            var vt = ViewTransform.CreateOrthographic(_GBuffer.Width, _GBuffer.Height);
            vt.Position = PendingFieldViewportPosition.GetValueOrDefault(Vector2.Zero);
            vt.Scale = PendingFieldViewportScale.GetValueOrDefault(Vector2.One) * Configuration.RenderScale;

            // FIXME: Is this right?
            using (var group = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex, _GBuffer.Texture,
                // FIXME: Optimize this
                (dm, _) => {
                    dm.PushStates();
                    Materials.PushViewTransform(ref vt);
                    dm.Device.ScissorRectangle = new Rectangle(0, 0, renderWidth, renderHeight);
                },
                (dm, _) => {
                    Materials.PopViewTransform();
                    dm.PopStates();
                }
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, -1, "LightingRenderer {0} : Begin G-Buffer", this.ToObjectID());

                ClearBatch.AddNew(
                    group, 0, Materials.Clear, 
                    Color.Transparent, clearZ: 0
                );

                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 1, IlluminantMaterials.GroundPlane,
                    (dm, _) => {
                        var p = IlluminantMaterials.HeightVolumeFrontFace.Effect.Parameters;
                        p["DistanceFieldExtent"].SetValue(Extent3);
                        p["SelfOcclusionHack"].SetValue(ComputeFrontSelfOcclusionHack());
                        p = IlluminantMaterials.HeightVolumeTopFace.Effect.Parameters;
                        p["DistanceFieldExtent"].SetValue(Extent3);
                        p["SelfOcclusionHack"].SetValue(ComputeTopSelfOcclusionHack());

                        Materials.TrySetBoundUniform(IlluminantMaterials.HeightVolumeFrontFace, "Environment", ref EnvironmentUniforms);
                        Materials.TrySetBoundUniform(IlluminantMaterials.HeightVolumeTopFace, "Environment", ref EnvironmentUniforms);
                        Materials.TrySetBoundUniform(IlluminantMaterials.GroundPlane, "Environment", ref EnvironmentUniforms);

                        dm.Device.RasterizerState = Render.Convenience.RenderStates.ScissorOnly;
                    }
                )) {
                    RenderGroundPlane(batch);

                    if (Configuration.TwoPointFiveD) {
                        RenderTwoPointFiveDVolumes(enableHeightVolumes, enableBillboards, group);
                    } else if (enableHeightVolumes) {
                        using (var batch2 = PrimitiveBatch<HeightVolumeVertex>.New(
                            group, 2, IlluminantMaterials.HeightVolumeTopFace
                        ))
                            RenderGBufferVolumes(batch2);
                    }
                }

                // TODO: Update the heightmap using any SDF light obstructions (maybe only if they're flagged?)

                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, 9999, "LightingRenderer {0} : End G-Buffer", this.ToObjectID());
            }
        }

        private void RenderGBufferVolumes (PrimitiveBatch<HeightVolumeVertex> batch) {
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

            if (enableHeightVolumes)
                using (var topBatch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 3, Materials.Get(
                        IlluminantMaterials.HeightVolumeTopFace,
                        depthStencilState: TopFaceDepthStencilState,
                        rasterizerState: Render.Convenience.RenderStates.ScissorOnly,
                        blendState: BlendState.Opaque
                    )
                ))
                using (var frontBatch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 5, Materials.Get(
                        IlluminantMaterials.HeightVolumeFrontFace,
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
                var huge = new Vector3(0, 0, -99999);
                tl += huge;
                tr += huge;
                br += huge;
                bl += huge;
            }

            var verts = new HeightVolumeVertex[] {
                new HeightVolumeVertex(tl, Vector3.UnitZ, zRange),
                new HeightVolumeVertex(tr, Vector3.UnitZ, zRange),
                new HeightVolumeVertex(br, Vector3.UnitZ, zRange),
                new HeightVolumeVertex(bl, Vector3.UnitZ, zRange)
            };

            batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                PrimitiveType.TriangleList, verts, 0, 4, QuadIndices, 0, 2
            ));
        }

        private void _SetTextureForGBufferBillboard (DeviceManager dm, ref PrimitiveDrawCall<BillboardVertex> drawCall, int index) {
            var material = dm.CurrentMaterial;
            material.Effect.Parameters["Mask"].SetValue((Texture)drawCall.UserData);
            material.Flush();
        }

        private struct GBufferBillboardSorter : IRefComparer<Billboard> {
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

        private short[] BillboardQuadIndices;

        private void RenderGBufferBillboards (IBatchContainer container, int layerIndex) {
            if (Environment.Billboards == null)
                return;

            // FIXME: This suuuuuuuuuuucks
            BillboardScratch.Clear();
            BillboardScratch.AddRange(Environment.Billboards);
            BillboardScratch.FastCLRSortRef(new GBufferBillboardSorter());

            BillboardVertexScratch.EnsureCapacity(BillboardScratch.Count * 4);
            BillboardVertexScratch.Clear();

            var verts = BillboardVertexScratch.GetBuffer();

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
                ), (dm, _) => {
                    var material = IlluminantMaterials.MaskBillboard;
                    // Materials.TrySetBoundUniform(material, "Viewport", ref viewTransform);
                    Materials.TrySetBoundUniform(material, "Environment", ref EnvironmentUniforms);
                    material.Effect.Parameters["DistanceFieldExtent"].SetValue(Extent3);
                    material.Effect.Parameters["SelfOcclusionHack"].SetValue(ComputeFrontSelfOcclusionHack());
                }
            )) 
            using (var gDataBatch = PrimitiveBatch<BillboardVertex>.New(
                container, layerIndex++, Materials.Get(
                    IlluminantMaterials.GDataBillboard,
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RenderStates.ScissorOnly,
                    blendState: BlendState.Opaque
                ), (dm, _) => {
                    var material = IlluminantMaterials.GDataBillboard;
                    // Materials.TrySetBoundUniform(material, "Viewport", ref viewTransform);
                    Materials.TrySetBoundUniform(material, "Environment", ref EnvironmentUniforms);
                    material.Effect.Parameters["DistanceFieldExtent"].SetValue(Extent3);
                    material.Effect.Parameters["SelfOcclusionHack"].SetValue(ComputeFrontSelfOcclusionHack());
                }
            )) {
                Action flushBatch = () => {
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
                };

                foreach (var billboard in BillboardScratch) {
                    if ((previousTexture != billboard.Texture) || (previousType != billboard.Type)) {
                        flushBatch();
                        runStartedAt = i;
                        runStartedAtVertex = j;
                        previousTexture = billboard.Texture;
                        previousType = billboard.Type;
                    }

                    var sb = billboard.ScreenBounds;
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

                flushBatch();
            }
        }

        private void SetGBufferParameters (EffectParameterCollection p) {
            // FIXME: RenderScale?
            if (_GBuffer != null) {
                p["GBufferTexelSize"].SetValue(_GBuffer.InverseSize);
                p["GBuffer"].SetValue(_GBuffer.Texture);
            } else {
                p["GBufferTexelSize"].SetValue(Vector2.Zero);
                p["GBuffer"].SetValue((Texture2D)null);
            }
        }
    }
}
