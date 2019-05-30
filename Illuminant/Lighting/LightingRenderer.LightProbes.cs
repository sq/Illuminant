using System;
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
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private          Texture2D     _LightProbePositions, _LightProbeNormals;
        private          BufferRing    _LightProbeValueBuffers;
        private readonly object        _LightProbeReadbackArrayLock = new object();
        private          HalfVector4[] _LightProbeReadbackArray;

        private void _LightProbeBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;

            device.SetViewport(new Viewport(0, 0, Probes.Count, 1));
            device.Device.BlendState = RenderStates.AdditiveBlend;

            SetLightShaderParameters(ltrs.ProbeMaterial, ltrs.Key.Quality);

            ltrs.ProbeMaterial.Effect.Parameters["GBuffer"].SetValue(_LightProbePositions);
            ltrs.ProbeMaterial.Effect.Parameters["GBufferTexelSizeAndMisc"].SetValue(new Vector4(
                1.0f / Configuration.MaximumLightProbeCount, 1.0f,
                1, 1
            ));
            ltrs.ProbeMaterial.Effect.Parameters["ProbeNormals"].SetValue(_LightProbeNormals);
            ltrs.ProbeMaterial.Effect.Parameters["RampTexture"].SetValue(ltrs.Key.RampTexture);
        }

        private void _EndLightProbePass (DeviceManager device, object userData) {
            Materials.PopViewTransform();
            device.PopStates();

            var buffer = (RenderTarget2D)userData;
            _LightProbeValueBuffers.MarkRenderComplete(buffer);
        }

        private void UpdateLightProbes (IBatchContainer container, int layer, RenderTarget2D renderTarget, bool isForGi, float intensityScale) {
            using (var lightProbeGroup = BatchGroup.ForRenderTarget(
                container, layer, renderTarget, 
                before: BeginLightProbePass, after: isForGi ? EndGIProbePass : EndLightProbePass,
                userData: renderTarget
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(lightProbeGroup, -2, "LightingRenderer {0} : Update {1} probes", this.ToObjectID(), isForGi ? "GI" : "Light");

                ClearBatch.AddNew(
                    lightProbeGroup, -1, Materials.Clear, Color.Transparent
                );

                int layerIndex = 0;
                foreach (var kvp in LightRenderStates) {
                    var ltrs = kvp.Value;
                    var count = ltrs.Count;
                    if (count <= 0)
                        continue;

                    if (RenderTrace.EnableTracing)
                        RenderTrace.Marker(lightProbeGroup, layerIndex++, "LightingRenderer {0} : Render {1} {2} light(s)", this.ToObjectID(), count, ltrs.Key.Type);

                    using (var nb = NativeBatch.New(
                        lightProbeGroup, layerIndex++, ltrs.ProbeMaterial, isForGi ? GIProbeBatchSetup : LightProbeBatchSetup, userData: ltrs
                    )) {
                        var cornerBuffer = ltrs.GetCornerBuffer(true);
                        nb.Add(new NativeDrawCall(
                            PrimitiveType.TriangleList,
                            cornerBuffer, 0,
                            ltrs.GetVertexBuffer(), 0,
                            null, 0,
                            QuadIndexBuffer, 0, 0, cornerBuffer.VertexCount, 0, cornerBuffer.VertexCount / 2, ltrs.LightCount
                        ));
                    }
                }
            }
        }

        private void UpdateLightProbeTexture () {
            using (var buffer = BufferPool<Vector4>.Allocate(Configuration.MaximumLightProbeCount)) {
                int x = 0;

                lock (Probes)
                foreach (var probe in Probes)
                    buffer.Data[x++] = new Vector4(probe._Position, 1);

                lock (Coordinator.UseResourceLock)
                    _LightProbePositions.SetData(buffer.Data, 0, Configuration.MaximumLightProbeCount);

                x = 0;

                lock (Probes)
                foreach (var probe in Probes) {
                    if (probe._Normal.HasValue)
                        buffer.Data[x++] = new Vector4(probe._Normal.Value, probe._EnableShadows ? 1 : 0);
                    else
                        buffer.Data[x++] = new Vector4(0, 0, 0, probe._EnableShadows ? 1 : 0);
                }

                lock (Coordinator.UseResourceLock)
                    _LightProbeNormals.SetData(buffer.Data, 0, Configuration.MaximumLightProbeCount);
            }
        }

#if FNA
        private struct LightProbeDownloadTask : IMainThreadWorkItem {
#else
        private struct LightProbeDownloadTask : IWorkItem {
#endif
            public LightingRenderer Renderer;
            public RenderTarget2D Texture;
            public long Timestamp;
            public float ScaleFactor;

            public void Execute () {
                var count = Renderer.Probes.Count;
                var now = Time.Ticks;

                lock (Renderer._LightProbeReadbackArrayLock) {
                    var buffer = Renderer._LightProbeReadbackArray;
                    if ((buffer == null) || (buffer.Length < (count)))
                        buffer = Renderer._LightProbeReadbackArray = new HalfVector4[count];

                    lock (Renderer.Coordinator.UseResourceLock)
                        Texture.GetData(
                            0, new Rectangle(0, 0, count, 1),
                            buffer, 0, count
                        );

                    int i = 0;

                    lock (Renderer.Probes)
                    foreach (var p in Renderer.Probes) {
                        if (p.UpdatedWhen >= Timestamp) {
                            i++;
                            continue;
                        }

                        p.PreviouslyUpdatedWhen = p.UpdatedWhen;
                        p.PreviousValue = p.Value;
                        p.UpdatedWhen = now;
                        p.Value = buffer[i++].ToVector4() * ScaleFactor;
                    }
                }

                return;
            }
        }
    }
}
