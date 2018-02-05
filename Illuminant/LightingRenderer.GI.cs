﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Tracing;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        internal const int SHValueCount = 9;

        public int GIProbeCount { get; private set; }
        private readonly RenderTarget2D _SelectedGIProbePositions, _SelectedGIProbeNormals;
        private readonly RenderTarget2D _GIProbeValues, _GIProbeSH;
        private bool _GIProbesDirty, _GIProbesWereSelected;
        private int _GIProbeCountX, _GIProbeCountY;

        private Vector3 LastGIProbeOffset;
        private Vector2 LastGIProbeInterval;

        internal int GIProbeNormalCount {
            get {
                return (int)Configuration.GIProbeQualityLevel;
            }
        }

        private void _EndGIProbePass (DeviceManager device, object userData) {
            Materials.PopViewTransform();
            device.PopStates();
        }        

        private void _GIProbeBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;

            device.Device.Viewport = new Viewport(0, 0, GIProbeCount, GIProbeNormalCount);
            device.Device.BlendState = RenderStates.AdditiveBlend;

            SetLightShaderParameters(ltrs.ProbeMaterial, ltrs.Key.Quality);

            ltrs.ProbeMaterial.Effect.Parameters["GBuffer"].SetValue(_SelectedGIProbePositions);
            ltrs.ProbeMaterial.Effect.Parameters["GBufferTexelSize"].SetValue(new Vector2(1.0f / _SelectedGIProbePositions.Width, 1.0f / GIProbeNormalCount));
            ltrs.ProbeMaterial.Effect.Parameters["ProbeNormals"].SetValue(_SelectedGIProbeNormals);
            ltrs.ProbeMaterial.Effect.Parameters["RampTexture"].SetValue(ltrs.Key.RampTexture);
        }

        /*
        private GIProbeRadiance? UnpackGIProbeRadiance (
            HalfVector4[] positions, HalfVector4[] normals, HalfVector4[] colors,
            int column, int row, float scaleFactor
        ) {
            var index = (Configuration.MaximumGIProbeCount * row) + column;
            var pos = positions[index].ToVector4();
            var norm = normals[index].ToVector4();

            if ((pos.W < 1) || (norm.W < 1))
                return null;

            return new GIProbeRadiance {
                Position = new Vector3(pos.X, pos.Y, pos.Z),
                SurfaceNormal = new Vector3(norm.X, norm.Y, norm.Z),
                Value = colors[index].ToVector4() * scaleFactor
            };
        }
        */

        private void UpdateGIProbes (IBatchContainer container, int layer) {
            if (!Configuration.EnableGlobalIllumination)
                return;

            using (var group = BatchGroup.New(container, layer, null, (dm, _) => {
                for (int i = 0; i < 16; i++)
                    dm.Device.Textures[i] = null;
            })) {
                if (
                    (LastGIProbeInterval != Environment.GIProbeInterval) ||
                    (LastGIProbeOffset != Environment.GIProbeOffset)
                )
                    _GIProbesDirty = true;

                if (_GIProbesDirty)
                    SelectGIProbes(group, 0);

                /*
                if (GIProbeCount == 0)
                    return;
                */

                UpdateLightProbes(group, 1, _GIProbeValues, true);
                UpdateGIProbeSH(group, 2);
            }
        }

        private void SelectGIProbes (IBatchContainer container, int layer) {
            var m = IlluminantMaterials.GIProbeSelector;
            var p = m.Effect.Parameters;

            var extent = Extent3;
            // FIXME: .Ceiling breaks really bad, WTF?
            _GIProbeCountX = (int)Math.Floor((extent.X - Environment.GIProbeOffset.X) / Environment.GIProbeInterval.X);
            _GIProbeCountY = (int)Math.Floor((extent.Y - Environment.GIProbeOffset.Y) / Environment.GIProbeInterval.Y);

            GIProbeCount = _GIProbeCountX * _GIProbeCountY;
            if (GIProbeCount > Configuration.MaximumGIProbeCount)
                GIProbeCount = Configuration.MaximumGIProbeCount;

            using (var rt = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    dm.PushRenderTargets(new RenderTargetBinding[] { _SelectedGIProbePositions, _SelectedGIProbeNormals });
                    dm.Device.Viewport = new Viewport(0, 0, GIProbeCount, GIProbeNormalCount);

                    SetDistanceFieldParameters(m, true, Configuration.GIProbeQuality);

                    p["ProbeOffset"].SetValue(Environment.GIProbeOffset);
                    p["ProbeInterval"].SetValue(Environment.GIProbeInterval);
                    p["ProbeCount"].SetValue(new Vector2(_GIProbeCountX, _GIProbeCountY));
                    p["NormalCount"].SetValue(GIProbeNormalCount);
                    p["BounceFalloffDistance"].SetValue(Configuration.GIBounceFalloffDistance);
                    p["BounceSearchDistance"].SetValue(Configuration.GIBounceSearchDistance);

                    m.Flush();
                },
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                    dm.PopRenderTarget();
                    _GIProbesDirty = false;
                    _GIProbesWereSelected = true;
                }
            ))
            using (var pb = PrimitiveBatch<GIProbeVertex>.New(rt, 1, m)) {
                RenderTrace.Marker(rt, 0, "Select GI probe locations");

                var pdc = new PrimitiveDrawCall<GIProbeVertex>(
                    PrimitiveType.TriangleList,
                    new [] {
                        new GIProbeVertex(-1, -1),
                        new GIProbeVertex(1, -1),
                        new GIProbeVertex(1, 1),
                        new GIProbeVertex(-1, 1)
                    },
                    0, 4, QuadIndices, 0, 2
                );
                pb.Add(ref pdc);
            }
        }

        private void UpdateGIProbeSH (IBatchContainer container, int layer) {
            var m = IlluminantMaterials.GIProbeSHGenerator;
            var p = m.Effect.Parameters;

            using (var rt = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    dm.PushRenderTarget(_GIProbeSH);
                    dm.Device.Viewport = new Viewport(0, 0, GIProbeCount, SHValueCount);

                    p["NormalCount"].SetValue(GIProbeNormalCount);
                    p["ProbeValuesTexelSize"].SetValue(new Vector2(1.0f / _GIProbeValues.Width, 1.0f / _GIProbeValues.Height));
                    p["ProbeValues"].SetValue((Texture2D)null);
                    p["ProbeValues"].SetValue(_GIProbeValues);

                    m.Flush();
                },
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                    dm.PopRenderTarget();
                }
            ))
            using (var pb = PrimitiveBatch<GIProbeVertex>.New(rt, 1, m)) {
                RenderTrace.Marker(rt, 0, "Update GI probe spherical harmonics");

                var pdc = new PrimitiveDrawCall<GIProbeVertex>(
                    PrimitiveType.TriangleList,
                    new [] {
                        new GIProbeVertex(-1, -1),
                        new GIProbeVertex(1, -1),
                        new GIProbeVertex(1, 1),
                        new GIProbeVertex(-1, 1)
                    },
                    0, 4, QuadIndices, 0, 2
                );
                pb.Add(ref pdc);
            }
        }

        public void VisualizeGIProbes (IBatchContainer container, int layer, float radius, float brightness = 1) {
            var m = Materials.Get(IlluminantMaterials.VisualizeGI, blendState: BlendState.AlphaBlend);
            var p = m.Effect.Parameters;

            using (var group = BatchGroup.New(
                container, layer,
                null,
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            ))
            using (var pb = PrimitiveBatch<VisualizeGIProbeVertex>.New(
                group, 1, m,
                (dm, _) => {
                    p["Brightness"].SetValue(brightness);
                    p["SphericalHarmonicsTexelSize"].SetValue(new Vector2(1.0f / _GIProbeSH.Width, 1.0f / _GIProbeSH.Height));
                    p["SphericalHarmonics"].SetValue((Texture2D)null);
                    p["SphericalHarmonics"].SetValue(_GIProbeSH);

                    m.Flush();
                }
            )) {
                RenderTrace.Marker(group, 0, "Visualize GI probes");

                var count = (short)GIProbeCount;
                var buf = new VisualizeGIProbeVertex[count * 6];

                for (short i = 0; i < count; i++) {
                    int y = i / _GIProbeCountX;
                    int x = i - (y * _GIProbeCountX);
                    Vector3 pos = Environment.GIProbeOffset + new Vector3(Environment.GIProbeInterval.X * x, Environment.GIProbeInterval.Y * y, 0);
                    var j = i * 6;
                    buf[j + 0] = new VisualizeGIProbeVertex(pos, -1, -1, i, radius); // 0
                    buf[j + 1] = buf[j + 3] = new VisualizeGIProbeVertex(pos, 1, -1, i, radius); // 1
                    buf[j + 4] = new VisualizeGIProbeVertex(pos, 1, 1, i, radius); // 2
                    buf[j + 2] = buf[j + 5] = new VisualizeGIProbeVertex(pos, -1, 1, i, radius); // 3                    
                }

                var pdc = new PrimitiveDrawCall<VisualizeGIProbeVertex>(
                    PrimitiveType.TriangleList,
                    buf, 0, count * 2
                );
                pb.Add(ref pdc);
            }
        }

        public void RenderGlobalIllumination (
            IBatchContainer container, int layer
        ) {
            var m = IlluminantMaterials.RenderGI;
            var p = m.Effect.Parameters;

            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    dm.Device.BlendState = BlendState.Opaque;

                    p["Brightness"].SetValue(1);
                    p["SphericalHarmonicsTexelSize"].SetValue(new Vector2(1.0f / _GIProbeSH.Width, 1.0f / _GIProbeSH.Height));
                    p["SphericalHarmonics"].SetValue((Texture2D)null);
                    p["SphericalHarmonics"].SetValue(_GIProbeSH);

                    SetLightShaderParameters(m, Configuration.DefaultQuality);

                    m.Flush();
                },
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            ))
            using (var pb = PrimitiveBatch<GIProbeVertex>.New(group, 1, m)) {
                RenderTrace.Marker(group, 0, "Render global illumination");

                var pdc = new PrimitiveDrawCall<GIProbeVertex>(
                    PrimitiveType.TriangleList,
                    new [] {
                        new GIProbeVertex(-1, -1),
                        new GIProbeVertex(1, -1),
                        new GIProbeVertex(1, 1),
                        new GIProbeVertex(-1, 1)
                    },
                    0, 4, QuadIndices, 0, 2
                );
                pb.Add(ref pdc);
            }
        }
    }

    public enum GIProbeQualityLevels : int {
        Low = 32,
        Medium = 48,
        High = 64
    }
}
