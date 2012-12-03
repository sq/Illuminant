﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant {
    public class LightingRenderer {
        public readonly Material DebugOutlines, Shadow, PointLight, ClearStencil;

        public static readonly short[] ShadowIndices;
        public static readonly short[] PointLightIndices;

        public LightingEnvironment Environment;

        static LightingRenderer () {
            ShadowIndices = new short[] {
                0, 1, 2,
                1, 2, 3
            };

            PointLightIndices = new short[] {
                0, 1, 3,
                1, 2, 3
            };
        }

        public LightingRenderer (ContentManager content, DefaultMaterialSet materials, LightingEnvironment environment) {
            ClearStencil = materials.Clear;

            materials.Add(DebugOutlines = new DelegateMaterial(
                materials.ScreenSpaceGeometry,
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => dm.Device.BlendState = BlendState.AlphaBlend
                    )
                },
                new Action<DeviceManager>[0]
            ));

            materials.Add(PointLight = new DelegateMaterial(
                new Squared.Render.EffectMaterial(
                    content.Load<Effect>("Illumination"), "PointLight"
                ),
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => {
                            dm.Device.BlendState = BlendState.Additive;
                            dm.Device.DepthStencilState = new DepthStencilState {
                                DepthBufferEnable = false,
                                StencilEnable = true,
                                StencilFunction = CompareFunction.Equal,
                                StencilPass = StencilOperation.Keep,
                                StencilFail = StencilOperation.Keep,
                                ReferenceStencil = 1
                            };
                            dm.Device.RasterizerState = RasterizerState.CullNone;
                        }
                    )
                },
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => dm.Device.DepthStencilState = DepthStencilState.None
                    )
                }
            ));

            materials.Add(Shadow = new DelegateMaterial(
                new Squared.Render.EffectMaterial(
                    content.Load<Effect>("Illumination"), "Shadow"
                ),
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => {
                            dm.Device.BlendState = BlendState.Opaque;
                            dm.Device.DepthStencilState = new DepthStencilState {
                                DepthBufferEnable = false,
                                StencilEnable = true,
                                StencilFunction = CompareFunction.Never,
                                StencilPass = StencilOperation.Keep,
                                StencilFail = StencilOperation.Zero
                            };
                            dm.Device.RasterizerState = RasterizerState.CullNone;
                        }
                    )
                },
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => dm.Device.DepthStencilState = DepthStencilState.None
                    )
                }
            ));

            Environment = environment;
        }

        public void RenderLighting (Frame frame, int layer) {
            using (var resultGroup = BatchGroup.New(frame, layer))
            for (var i = 0; i < Environment.LightSources.Count; i++) {
                using (var lightGroup = BatchGroup.New(resultGroup, i)) {
                    var lightSource = Environment.LightSources[i];

                    ClearBatch.AddNew(lightGroup, 0, ClearStencil, clearStencil: 1);

                    var vertexCount = Environment.Obstructions.Count * 4;

                    using (var pb = PrimitiveBatch<ShadowVertex>.New(lightGroup, 1, Shadow))
                    using (var buffer = pb.CreateBuffer(vertexCount)) {
                        ShadowVertex vertex;

                        foreach (var obstruction in Environment.Obstructions) {
                            var writer = buffer.GetWriter(4);

                            vertex.A = obstruction.A;
                            vertex.B = obstruction.B;
                            vertex.Light = lightSource.Position;

                            for (var j = 0; j < 4; j++) {
                                vertex.CornerIndex = j;
                                writer.Write(ref vertex);
                            }

                            pb.Add(writer.GetDrawCall(PrimitiveType.TriangleList, ShadowIndices, 0, ShadowIndices.Length));
                        }
                    }

                    using (var pb = PrimitiveBatch<PointLightVertex>.New(lightGroup, 2, PointLight))
                    using (var buffer = pb.CreateBuffer(4)) {
                        var writer = buffer.GetWriter(4);
                        PointLightVertex vertex;

                        vertex.LightCenter = lightSource.Position;
                        vertex.Color = lightSource.Color;
                        vertex.Ramp = new Vector2(lightSource.RampStart, lightSource.RampEnd);

                        vertex.Position = new Vector2(
                            lightSource.Position.X - lightSource.RampEnd, 
                            lightSource.Position.Y - lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        vertex.Position = new Vector2(
                            lightSource.Position.X + lightSource.RampEnd,
                            lightSource.Position.Y - lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        vertex.Position = new Vector2(
                            lightSource.Position.X + lightSource.RampEnd,
                            lightSource.Position.Y + lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        vertex.Position = new Vector2(
                            lightSource.Position.X - lightSource.RampEnd,
                            lightSource.Position.Y + lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        pb.Add(writer.GetDrawCall(PrimitiveType.TriangleList, PointLightIndices, 0, PointLightIndices.Length));
                    }
                }
            }
        }

        public void RenderOutlines (Frame frame, int layer, bool showLights) {
            using (var group = BatchGroup.New(frame, layer)) {
                using (var gb = GeometryBatch<VertexPositionColor>.New(group, 0, DebugOutlines)) {
                    foreach (var lo in Environment.Obstructions)
                        gb.AddLine(lo.A, lo.B, Color.White);
                }

                if (showLights)
                for (var i = 0; i < Environment.LightSources.Count; i++) {
                    var lightSource = Environment.LightSources[i];

                    var cMax = new Color(1f, 1f, 1f, 1f);
                    var cMin = cMax * 0.25f;

                    using (var gb = GeometryBatch<VertexPositionColor>.New(group, i + 1, DebugOutlines)) {
                        gb.AddFilledRing(lightSource.Position, 0f, 2f, cMax, cMax);
                        gb.AddFilledRing(lightSource.Position, lightSource.RampStart - 1f, lightSource.RampStart + 1f, cMax, cMax);
                        gb.AddFilledRing(lightSource.Position, lightSource.RampEnd - 1f, lightSource.RampEnd + 1f, cMin, cMin);
                    }
                }
            }
        }
    }
}
