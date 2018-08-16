using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;
using Squared.Render.Tracing;

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

        private void RenderGBuffer (
            ref int layerIndex, IBatchContainer resultGroup,
            int renderWidth, int renderHeight,
            bool enableHeightVolumes = true, bool enableBillboards = true
        ) {
            // FIXME: Is this right?
            using (var group = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex, _GBuffer.Texture,
                // FIXME: Optimize this
                (dm, _) => {
                    dm.PushStates();
                    PushLightingViewTransform(_GBuffer.Texture);
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
                    group, 1, IlluminantMaterials.HeightVolume,
                    (dm, _) => {
                        var p = IlluminantMaterials.HeightVolumeFace.Effect.Parameters;
                        p["DistanceFieldExtent"].SetValue(Extent3);

                        /*
                        Materials.TrySetBoundUniform(IlluminantMaterials.HeightVolumeFace, "Viewport", ref viewTransform);
                        Materials.TrySetBoundUniform(IlluminantMaterials.HeightVolume, "Viewport", ref viewTransform);
                        */

                        var ub = Materials.GetUniformBinding<Uniforms.Environment>(IlluminantMaterials.HeightVolumeFace, "Environment");
                        ub.Value.Current = EnvironmentUniforms;

                        ub = Materials.GetUniformBinding<Uniforms.Environment>(IlluminantMaterials.HeightVolume, "Environment");
                        ub.Value.Current = EnvironmentUniforms;
                    }
                )) {

                    // HACK: Fill in the gbuffer values for the ground plane
                    {
                        var zRange = new Vector2(Environment.GroundZ, Environment.GroundZ);
                        var tl = new Vector3(0, 0, Environment.GroundZ);
                        var tr = new Vector3(renderWidth, 0, Environment.GroundZ);
                        var br = new Vector3(renderWidth, renderHeight, Environment.GroundZ);
                        var bl = new Vector3(0, renderHeight, Environment.GroundZ);

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

                    if (Configuration.TwoPointFiveD) {
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
                                IlluminantMaterials.HeightVolumeFace,
                                depthStencilState: TopFaceDepthStencilState,
                                rasterizerState: RasterizerState.CullNone,
                                blendState: BlendState.Opaque
                            )
                        ))
                        using (var frontBatch = PrimitiveBatch<HeightVolumeVertex>.New(
                            group, 5, Materials.Get(
                                IlluminantMaterials.HeightVolumeFace,
                                depthStencilState: FrontFaceDepthStencilState,
                                rasterizerState: RasterizerState.CullNone,
                                blendState: BlendState.Opaque
                            )
                        ))
                        {
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

                    } else if (enableHeightVolumes) {
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
                }

                // TODO: Update the heightmap using any SDF light obstructions (maybe only if they're flagged?)

                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, 9999, "LightingRenderer {0} : End G-Buffer", this.ToObjectID());
            }
        }

        private void _SetTextureForGBufferBillboard (DeviceManager dm, ref PrimitiveDrawCall<BillboardVertex> drawCall, int index) {
            var material = dm.CurrentMaterial;
            material.Effect.Parameters["Mask"].SetValue((Texture)drawCall.UserData);
            material.Flush();
            var ub = Materials.GetUniformBinding<Uniforms.Environment>(material, "Environment");
            ub.Value.Current = EnvironmentUniforms;
        }

        private void RenderGBufferBillboards (IBatchContainer container, int layerIndex) {
            if (Environment.Billboards == null)
                return;

            // FIXME: This suuuuuuuuuuucks
            BillboardScratch.Clear();
            BillboardScratch.AddRange(Environment.Billboards);

            BillboardVertexScratch.EnsureCapacity(BillboardScratch.Count * 4);
            BillboardVertexScratch.Clear();

            var verts = BillboardVertexScratch.GetBuffer();

            int i = 0, j = 0;

            using (var maskBatch = PrimitiveBatch<BillboardVertex>.New(
                container, layerIndex++, Materials.Get(
                    IlluminantMaterials.MaskBillboard,
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RasterizerState.CullNone,
                    blendState: BlendState.Opaque
                ), (dm, _) => {
                    var material = IlluminantMaterials.MaskBillboard;
                    // Materials.TrySetBoundUniform(material, "Viewport", ref viewTransform);
                    Materials.TrySetBoundUniform(material, "Environment", ref EnvironmentUniforms);
                    material.Effect.Parameters["DistanceFieldExtent"].SetValue(Extent3);
                }
            )) 
            using (var gDataBatch = PrimitiveBatch<BillboardVertex>.New(
                container, layerIndex++, Materials.Get(
                    IlluminantMaterials.GDataBillboard,
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RasterizerState.CullNone,
                    blendState: BlendState.Opaque
                ), (dm, _) => {
                    var material = IlluminantMaterials.GDataBillboard;
                    // Materials.TrySetBoundUniform(material, "Viewport", ref viewTransform);
                    Materials.TrySetBoundUniform(material, "Environment", ref EnvironmentUniforms);
                    material.Effect.Parameters["DistanceFieldExtent"].SetValue(Extent3);
                }
            )) 
            foreach (var billboard in Environment.Billboards) {
                var tl   = billboard.Position;
                var size = billboard.Size;
                var normal1 = billboard.Normal;
                var normal2 = normal1;
                var bl = tl + new Vector3(0, size.Y, 0);
                var tr = tl + new Vector3(size.X, 0, 0);
                var dataScale = billboard.DataScale.GetValueOrDefault(1);

                // FIXME: Linear filtering = not a cylinder?
                if (Math.Abs(billboard.CylinderFactor) >= 0.001f) {
                    normal1.X = 0.5f - (0.5f * billboard.CylinderFactor);
                    normal2.X = 0.5f + (0.5f * billboard.CylinderFactor);
                }

                var textureBounds = billboard.TextureBounds;
                verts[j++] = new BillboardVertex {
                    Position = tl,
                    Normal = normal1,
                    WorldPosition = bl + new Vector3(0, 0, size.Z),
                    TexCoord = textureBounds.TopLeft,
                    DataScale = dataScale,
                };
                verts[j++] = new BillboardVertex {
                    Position = tr,
                    Normal = normal2,
                    WorldPosition = bl + new Vector3(size.X, 0, size.Z),
                    TexCoord = textureBounds.TopRight,
                    DataScale = dataScale,
                };
                verts[j++] = new BillboardVertex {
                    Position = tl + size,
                    Normal = normal2,
                    WorldPosition = bl + new Vector3(size.X, 0, 0),
                    TexCoord = textureBounds.BottomRight,
                    DataScale = dataScale,
                };
                verts[j++] = new BillboardVertex {
                    Position = bl,
                    Normal = normal1,
                    WorldPosition = bl,
                    TexCoord = textureBounds.BottomLeft,
                    DataScale = dataScale,
                };

                var batch = billboard.Type == BillboardType.GBufferData
                    ? gDataBatch
                    : maskBatch;
                
                batch.Add(new PrimitiveDrawCall<BillboardVertex>(
                    PrimitiveType.TriangleList, verts, i * 4, 4, 
                    QuadIndices, 0, 2, 
                    new DrawCallSortKey(order: i),
                    SetTextureForGBufferBillboard, billboard.Texture
                ));

                i++;
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
