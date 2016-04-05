﻿// #define SHADOW_VIZ

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed class LightingRenderer : IDisposable {
        public const int MaximumLightCount = 8192;
        public const int PackedSliceCount = 3;

        const int        DistanceLimit = 520;

        // HACK: If your projection matrix and your actual viewport/RT don't match in dimensions, you need to set this to compensate. :/
        // Scissor rects are fussy.
        public readonly DefaultMaterialSet Materials;
        public readonly RenderCoordinator Coordinator;
        public readonly IlluminantMaterials IlluminantMaterials;
        public readonly Render.EffectMaterial SphereLightMaterialInner;
        public readonly DepthStencilState TopFaceDepthStencilState, FrontFaceDepthStencilState;
        public readonly DepthStencilState DistanceInteriorStencilState, DistanceExteriorStencilState;
        public readonly DepthStencilState SphereLightDepthStencilState;
        public readonly BlendState[]      PackedSlice      = new BlendState[4];
        public readonly BlendState[]      ClearPackedSlice = new BlendState[4];

        private readonly DynamicVertexBuffer SphereLightVertexBuffer;
        private readonly IndexBuffer         SphereLightIndexBuffer;
        private readonly SphereLightVertex[] SphereLightVertices = new SphereLightVertex[MaximumLightCount * 4];

        private readonly Dictionary<Polygon, Texture2D> HeightVolumeVertexData = 
            new Dictionary<Polygon, Texture2D>(new ReferenceComparer<Polygon>());

        private readonly RenderTarget2D _GBuffer;
        private readonly RenderTarget2D _DistanceField;
        private readonly RenderTarget2D _Lightmap;
        private readonly RenderTarget2D _PreviousLightmap;

        private readonly Action<DeviceManager, object> BeginLightPass, EndLightPass, IlluminationBatchSetup;

        private readonly object _LightBufferLock = new object();

        public readonly RendererConfiguration Configuration;
        public LightingEnvironment Environment;

        private readonly List<int> _InvalidDistanceFieldSlices = new List<int>();

        // HACK
        private int         _DistanceFieldSlicesReady = 0;
        private bool        _GBufferReady     = false;

        public readonly int DistanceFieldSliceWidth, DistanceFieldSliceHeight;
        public readonly int DistanceFieldSlicesX, DistanceFieldSlicesY;

        public LightingRenderer (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, LightingEnvironment environment,
            RendererConfiguration configuration
        ) {
            Materials = materials;
            Coordinator = coordinator;
            Configuration = configuration;

            IlluminantMaterials = new IlluminantMaterials(materials);

            BeginLightPass     = _BeginLightPass;
            EndLightPass       = _EndLightPass;
            IlluminationBatchSetup = _IlluminationBatchSetup;

            lock (coordinator.CreateResourceLock) {
                SphereLightVertexBuffer = new DynamicVertexBuffer(
                    coordinator.Device, typeof(SphereLightVertex), 
                    SphereLightVertices.Length, BufferUsage.WriteOnly
                );
                SphereLightIndexBuffer = new IndexBuffer(
                    coordinator.Device, IndexElementSize.SixteenBits, MaximumLightCount * 6, BufferUsage.WriteOnly
                );

                FillIndexBuffer();

                DistanceFieldSliceWidth = (int)(Configuration.DistanceFieldSize.First * Configuration.DistanceFieldResolution);
                DistanceFieldSliceHeight = (int)(Configuration.DistanceFieldSize.Second * Configuration.DistanceFieldResolution);
                int maxSlicesX = 4096 / DistanceFieldSliceWidth;
                int maxSlicesY = 4096 / DistanceFieldSliceHeight;
                // HACK: We encode odd/even slices in the red and green channels
                int maxSlices = maxSlicesX * maxSlicesY * PackedSliceCount;
                
                // HACK: If they ask for too many slices we give them as many as we can.
                int numSlices = Math.Min(Configuration.DistanceFieldSize.Third, maxSlices);
                Configuration.DistanceFieldSliceCount = numSlices;

                int effectiveSliceCount = (int)Math.Ceiling(numSlices / (float)PackedSliceCount);

                DistanceFieldSlicesX = Math.Min(maxSlicesX, effectiveSliceCount);
                DistanceFieldSlicesY = Math.Max((int)Math.Ceiling(effectiveSliceCount / (float)maxSlicesX), 1);

                _DistanceField = new RenderTarget2D(
                    coordinator.Device,
                    DistanceFieldSliceWidth * DistanceFieldSlicesX, 
                    DistanceFieldSliceHeight * DistanceFieldSlicesY,
                    false, 
                    SurfaceFormat.Rgba64,
                    DepthFormat.None, 0, 
                    RenderTargetUsage.PlatformContents
                );

                _GBuffer = new RenderTarget2D(
                    coordinator.Device, 
                    Configuration.MaximumRenderSize.First, 
                    Configuration.MaximumRenderSize.Second,
                    false, 
                    Configuration.HighQuality
                        ? SurfaceFormat.Vector4
                        : SurfaceFormat.HalfVector4,
                    DepthFormat.Depth24, 0, RenderTargetUsage.PlatformContents
                );

                _Lightmap = new RenderTarget2D(
                    coordinator.Device, 
                    Configuration.MaximumRenderSize.First, 
                    Configuration.MaximumRenderSize.Second,
                    false,
                    Configuration.HighQuality
                        ? SurfaceFormat.Rgba64
                        : SurfaceFormat.Color,
                    DepthFormat.None, 0, RenderTargetUsage.PlatformContents
                );

                if (Configuration.EnableBrightnessEstimation)
                    _PreviousLightmap = new RenderTarget2D(
                        coordinator.Device, 
                        Configuration.MaximumRenderSize.First / 2, 
                        Configuration.MaximumRenderSize.Second / 2,
                        true,
                        Configuration.HighQuality
                            ? SurfaceFormat.Rgba64
                            : SurfaceFormat.Color,
                        DepthFormat.None, 0, RenderTargetUsage.PlatformContents
                    );
            }

            TopFaceDepthStencilState = new DepthStencilState {
                StencilEnable = false,
                DepthBufferEnable = true,
                DepthBufferFunction = CompareFunction.GreaterEqual,
                DepthBufferWriteEnable = true
            };
            
            FrontFaceDepthStencilState = new DepthStencilState {
                StencilEnable = false,
                DepthBufferEnable = true,
                DepthBufferFunction = CompareFunction.GreaterEqual,
                DepthBufferWriteEnable = true
            };
            
            SphereLightDepthStencilState = new DepthStencilState {
                StencilEnable = false,
                DepthBufferEnable = false
            };

            for (int i = 0; i < 4; i++) {
                var writeChannels = (ColorWriteChannels)(1 << i);

                PackedSlice[i] = new BlendState {
                    ColorWriteChannels = writeChannels,
                    AlphaBlendFunction = BlendFunction.Max,
                    AlphaSourceBlend = Blend.One,
                    AlphaDestinationBlend = Blend.One,
                    ColorBlendFunction = BlendFunction.Max,
                    ColorSourceBlend = Blend.One,
                    ColorDestinationBlend = Blend.One
                };

                ClearPackedSlice[i] = new BlendState {
                    ColorWriteChannels = writeChannels,
                    AlphaBlendFunction = BlendFunction.Add,
                    AlphaSourceBlend = Blend.One,
                    AlphaDestinationBlend = Blend.Zero,
                    ColorBlendFunction = BlendFunction.Add,
                    ColorSourceBlend = Blend.One,
                    ColorDestinationBlend = Blend.Zero
                };
            };

            {
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone,
                        depthStencilState: SphereLightDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                SphereLightMaterialInner =
                    new Render.EffectMaterial(content.Load<Effect>("Illumination"), "SphereLight");

                materials.Add(IlluminantMaterials.SphereLight = new DelegateMaterial(
                    SphereLightMaterialInner, dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.DistanceFieldExterior = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("DistanceField"), "Exterior"));

                materials.Add(IlluminantMaterials.DistanceFieldInterior = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("DistanceField"), "Interior"));

                materials.Add(IlluminantMaterials.DistanceFunction = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("DistanceFunction"), "DistanceFunction"));

                materials.Add(IlluminantMaterials.HeightVolume = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("GBuffer"), "HeightVolume"));

                materials.Add(IlluminantMaterials.HeightVolumeFace = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("GBuffer"), "HeightVolumeFace"));

                materials.Add(IlluminantMaterials.MaskBillboard = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("GBufferBitmap"), "MaskBillboard"));

                materials.Add(IlluminantMaterials.GDataBillboard = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("GBufferBitmap"), "GDataBillboard"));

                materials.Add(IlluminantMaterials.LightingResolve = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("Resolve"), "LightingResolve"));

                materials.Add(IlluminantMaterials.GammaCompressedLightingResolve = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("Resolve"), "GammaCompressedLightingResolve"));

                materials.Add(IlluminantMaterials.ToneMappedLightingResolve = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("Resolve"), "ToneMappedLightingResolve"));
            }

            materials.Add(IlluminantMaterials.ScreenSpaceGammaCompressedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("HDRBitmap"), "ScreenSpaceGammaCompressedBitmap"
            ));

            materials.Add(IlluminantMaterials.WorldSpaceGammaCompressedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("HDRBitmap"), "WorldSpaceGammaCompressedBitmap"
            ));

            materials.Add(IlluminantMaterials.ScreenSpaceToneMappedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("HDRBitmap"), "ScreenSpaceToneMappedBitmap"
            ));

            materials.Add(IlluminantMaterials.WorldSpaceToneMappedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("HDRBitmap"), "WorldSpaceToneMappedBitmap"
            ));

            Environment = environment;

            Coordinator.DeviceReset += Coordinator_DeviceReset;
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            InvalidateFields();
        }

        private void FillIndexBuffer () {
            var buf = new short[SphereLightIndexBuffer.IndexCount];
            int i = 0, j = 0;
            while (i < buf.Length) {
                buf[i++] = (short)(j + 0);
                buf[i++] = (short)(j + 1);
                buf[i++] = (short)(j + 3);
                buf[i++] = (short)(j + 1);
                buf[i++] = (short)(j + 2);
                buf[i++] = (short)(j + 3);

                j += 4;
            }

            SphereLightIndexBuffer.SetData(buf);
        }

        public void Dispose () {
            Coordinator.DisposeResource(SphereLightVertexBuffer);
            Coordinator.DisposeResource(SphereLightIndexBuffer);
            Coordinator.DisposeResource(_DistanceField);
            Coordinator.DisposeResource(_GBuffer);
            Coordinator.DisposeResource(_Lightmap);
            Coordinator.DisposeResource(_PreviousLightmap);

            foreach (var kvp in HeightVolumeVertexData)
                Coordinator.DisposeResource(kvp.Value);
        }

        private const int LuminanceScaleFactor = 8192;
        private const int RedScaleFactor = (int)(0.299 * LuminanceScaleFactor);
        private const int GreenScaleFactor = (int)(0.587 * LuminanceScaleFactor);
        private const int BlueScaleFactor = (int)(0.114 * LuminanceScaleFactor);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AnalyzeStep (
            int red, int green, int blue,
            int luminanceThreshold,
            ref int overThresholdCount, ref long sum, 
            ref int min, ref int max
        ) {
            int luminance = ((red * RedScaleFactor) + (green * GreenScaleFactor) + (blue * BlueScaleFactor)) / 65536;
            min = Math.Min(min, luminance);
            max = Math.Max(max, luminance);
            sum += luminance;

            if (luminance >= luminanceThreshold)
                overThresholdCount += 1;
        }

        private unsafe LightmapInfo AnalyzeLightmap (
            byte[] buffer, int count, 
            float scaleFactor, float threshold
        ) {
            int overThresholdCount = 0, min = int.MaxValue, max = 0;
            long sum = 0;

            int pixelCount = count / 
                (Configuration.HighQuality
                    ? 8
                    : 4);

            int luminanceThreshold = (int)(threshold * LuminanceScaleFactor / scaleFactor);

            fixed (byte* pBuffer = buffer)
            if (Configuration.HighQuality) {
                var pRgba = (ushort*)pBuffer;

                for (int i = 0; i < pixelCount; i++, pRgba += 4) {
                    AnalyzeStep(
                        pRgba[0], pRgba[1], pRgba[2],
                        luminanceThreshold,
                        ref overThresholdCount, ref sum,
                        ref min, ref max
                    );
                }                
            } else {
                var pRgba = pBuffer;

                for (int i = 0; i < pixelCount; i++, pRgba += 4) {
                    AnalyzeStep(
                        pRgba[0] * 257, pRgba[1] * 257, pRgba[2] * 257,
                        luminanceThreshold,
                        ref overThresholdCount, ref sum,
                        ref min, ref max
                    );
                }                
            }

            var effectiveScaleFactor = (1.0f / LuminanceScaleFactor) * scaleFactor;

            return new LightmapInfo {
                Overexposed = overThresholdCount / (float)pixelCount,
                Minimum = min * effectiveScaleFactor,
                Maximum = max * effectiveScaleFactor,
                Mean = (sum * effectiveScaleFactor) / pixelCount
            };
        }

        /// <summary>
        /// Analyzes the internal lighting buffer. This operation is asynchronous so that you do not stall on
        ///  a previous/in-flight draw operation.
        /// </summary>
        /// <param name="scaleFactor">Scale factor for the lighting values (you want 1.0f / intensityFactor, probably)</param>
        /// <param name="threshold">Threshold for overexposed values (after scaling). 1.0f is reasonable.</param>
        /// <param name="accuracyFactor">Governs how many pixels will be analyzed. Higher values are lower accuracy (but faster).</param>
        /// <returns>LightmapInfo containing the minimum, average, and maximum light values, along with an overexposed pixel ratio [0-1].</returns>
        public async Task<LightmapInfo> EstimateBrightness (
            float scaleFactor, float threshold, 
            int accuracyFactor = 3
        ) {
            if (!Configuration.EnableBrightnessEstimation)
                throw new InvalidOperationException("Brightness estimation must be enabled");

            var levelIndex = Math.Min(accuracyFactor, _PreviousLightmap.LevelCount - 1);
            var divisor = (int)Math.Pow(2, levelIndex);
            var levelWidth = _PreviousLightmap.Width / divisor;
            var levelHeight = _PreviousLightmap.Height / divisor;
            var count = levelWidth * levelHeight * 
                (Configuration.HighQuality
                    ? 8
                    : 4);

            using (var buffer = BufferPool<byte>.Allocate(count)) {
                // TODO: Replace this with some sort of 'BeforePresent' primitive?
                return await Task.Run(() => {
                    // HACK: We lock on the lightmap to avoid directly trampling over a draw operation on the lightmap
                    lock (_PreviousLightmap)
                        _PreviousLightmap.GetData(
                            levelIndex, null,
                            buffer.Data, 0, count
                        );

                    return AnalyzeLightmap(buffer, count, scaleFactor, threshold);
                });
            }
        }

        public RenderTarget2D GBuffer {
            get {
                return _GBuffer;
            }
        }

        public RenderTarget2D DistanceField {
            get {
                return _DistanceField;
            }
        }

        public RenderTarget2D Lightmap {
            get {
                return _Lightmap;
            }
        }

        private void OnLightPassCompleted (RenderTimer timer) {
            // HACK: This timer needs to exist so the distance field timer is accurate
            return;

            var cpuDurationMs = timer.CPUDuration.GetValueOrDefault(0) * 1000;
            var gpuDurationMs = timer.GPUDuration.GetValueOrDefault(0) * 1000;

            Console.WriteLine(
                "Light pass ~{0:0000.0}ms", gpuDurationMs
            );
        }

        private void OnDistanceFieldCompleted (RenderTimer timer) {
            // HACK: This timer needs to exist so the light pass timer is accurate, if used

            var cpuDurationMs = timer.CPUDuration.GetValueOrDefault(0) * 1000;
            var gpuDurationMs = timer.GPUDuration.GetValueOrDefault(0) * 1000;

            Console.WriteLine(
                "Distance field ~{0:0000.0}ms", gpuDurationMs
            );
        }

        private void _BeginLightPass (DeviceManager device, object userData) {
            var vt = ViewTransform.CreateOrthographic(
                _Lightmap.Width, _Lightmap.Height
            );

            device.PushStates();
            Materials.PushViewTransform(ref vt);
        }

        private void _EndLightPass (DeviceManager device, object userData) {
            Materials.PopViewTransform();
            device.PopStates();
        }

        private void _IlluminationBatchSetup (DeviceManager device, object userData) {
            var lightCount = (int)userData;
            lock (_LightBufferLock)
                SphereLightVertexBuffer.SetData(SphereLightVertices, 0, lightCount * 4, SetDataOptions.Discard);

            device.Device.BlendState = RenderStates.AdditiveBlend;

            var mi = SphereLightMaterialInner;
            var tsize = new Vector2(
                1f / Configuration.RenderSize.First, 
                1f / Configuration.RenderSize.Second
            );
            mi.Effect.Parameters["GBufferTexelSize"].SetValue(tsize);
            mi.Effect.Parameters["GBuffer"].SetValue(GBuffer);

            mi.Effect.Parameters["GroundZ"].SetValue(Environment.GroundZ);
            mi.Effect.Parameters["ZToYMultiplier"].SetValue(
                Configuration.TwoPointFiveD
                    ? Environment.ZToYMultiplier
                    : 0.0f
            );

            mi.Effect.Parameters["Time"].SetValue((float)Time.Seconds);

            SetDistanceFieldParameters(mi.Effect.Parameters, true);
        }

        /// <summary>
        /// Updates the lightmap in the target batch container on the specified layer.
        /// To display lighting, use ResolveLighting.
        /// </summary>
        /// <param name="container">The batch container to render lighting into.</param>
        /// <param name="layer">The layer to render lighting into.</param>
        /// <param name="intensityScale">A factor to scale the intensity of all light sources. You can use this to rescale the intensity of light values for HDR.</param>
        public void RenderLighting (IBatchContainer container, int layer, float intensityScale = 1.0f) {
            int layerIndex = 0;

            using (var outerGroup = BatchGroup.New(container, layer)) {
                using (var resultGroup = BatchGroup.ForRenderTarget(outerGroup, 0, _Lightmap, before: BeginLightPass, after: EndLightPass)) {
                    if (Render.Tracing.RenderTrace.EnableTracing)
                        Render.Tracing.RenderTrace.Marker(resultGroup, -9999, "LightingRenderer {0:X4} : Begin", this.GetHashCode());

                    int lightCount = Environment.Lights.Count;

                    ClearBatch.AddNew(
                        resultGroup, -1, Materials.Clear, new Color(0, 0, 0, Configuration.RenderGroundPlane ? 1f : 0f)
                    );

                    lock (_LightBufferLock)
                    Parallel.For(0, lightCount, (i) => {
                        var lightSource = Environment.Lights[i];

                        float radius = lightSource.Radius + lightSource.RampLength;
                        var lightBounds3 = lightSource.Bounds;
                        var lightBounds = lightBounds3.XY;

                        // Expand the bounding box upward to account for 2.5D perspective
                        if (Configuration.TwoPointFiveD) {
                            var offset = Math.Min(
                                lightSource.Radius + lightSource.RampLength,
                                Environment.MaximumZ
                            );
                            // FIXME: Is this right?
                            lightBounds.TopLeft.Y -= (offset / Environment.ZToYMultiplier);
                        }

                        lightBounds = lightBounds.Scale(Configuration.RenderScale);

                        SphereLightVertex vertex;
                        vertex.LightCenterAndAO = new Vector4(
                            lightSource.Position,
                            lightSource.AmbientOcclusionRadius
                        );
                        vertex.Color = lightSource.Color;
                        vertex.Color.W *= (lightSource.Opacity * intensityScale);
                        vertex.LightProperties = new Vector4(
                            lightSource.Radius, lightSource.RampLength,
                            (float)(int)lightSource.RampMode,
                            lightSource.CastsShadows ? 1f : 0f
                        );

                        int j = i * 4;
                        vertex.Position = lightBounds.TopLeft;
                        SphereLightVertices[j++] = vertex;

                        vertex.Position = lightBounds.TopRight;
                        SphereLightVertices[j++] = vertex;

                        vertex.Position = lightBounds.BottomRight;
                        SphereLightVertices[j++] = vertex;

                        vertex.Position = lightBounds.BottomLeft;
                        SphereLightVertices[j++] = vertex;
                    });

                    if (lightCount > 0) {
                        if (Render.Tracing.RenderTrace.EnableTracing)
                            Render.Tracing.RenderTrace.Marker(resultGroup, layerIndex++, "LightingRenderer {0:X4} : Render {1} light source(s)", this.GetHashCode(), lightCount);

                        using (var nb = NativeBatch.New(
                            resultGroup, layerIndex++, IlluminantMaterials.SphereLight, IlluminationBatchSetup, userData: lightCount
                        ))
                            nb.Add(new NativeDrawCall(
                                PrimitiveType.TriangleList,
                                SphereLightVertexBuffer, 0,
                                SphereLightIndexBuffer, 0, 0, lightCount * 4, 0, lightCount * 2
                            ));
                    }

                    if (Render.Tracing.RenderTrace.EnableTracing)
                        Render.Tracing.RenderTrace.Marker(resultGroup, 9999, "LightingRenderer {0:X4} : End", this.GetHashCode());
                }

                // HACK: We make a copy of the new lightmap so that brightness estimation can read it, without
                //  stalling on the current lightmap being rendered
                if (Configuration.EnableBrightnessEstimation)
                using (var copyGroup = BatchGroup.ForRenderTarget(
                    outerGroup, 1, _PreviousLightmap,
                    (dm, _) => {
                        // HACK: We lock on the lightmap during the actual draw operation, just to be sure
                        Monitor.Enter(_PreviousLightmap);
                        Materials.PushViewTransform(ViewTransform.CreateOrthographic(_PreviousLightmap.Width, _PreviousLightmap.Height));
                    },
                    (dm, _) => {
                        Materials.PopViewTransform();
                        Monitor.Exit(_PreviousLightmap);
                    }
                )) {
                    if (Render.Tracing.RenderTrace.EnableTracing)
                        Render.Tracing.RenderTrace.Marker(copyGroup, -1, "LightingRenderer {0:X4} : Generate HDR Buffer", this.GetHashCode());

                    var ir = new ImperativeRenderer(
                        copyGroup, Materials, 
                        blendState: BlendState.Opaque,
                        samplerState: SamplerState.LinearClamp
                    );
                    ir.Clear(color: Color.Transparent);
                    ir.Draw(_Lightmap, Vector2.Zero, scale: new Vector2(0.5f, 0.5f));
                }
            }
        }

        /// <summary>
        /// Resolves the current lightmap into the specified batch container on the specified layer.
        /// The provided draw call determines the position and size of the resolved lightmap.
        /// If the provided draw call's texture is not LightingRenderer.Lightmap, it will be modulated by the resolved lightmap.
        /// </summary>
        /// <param name="container">The batch container to resolve lighting into.</param>
        /// <param name="layer">The layer to resolve lighting into.</param>
        /// <param name="drawCall">A draw call used as a template to resolve the lighting.</param>
        public void ResolveLighting (IBatchContainer container, int layer, BitmapDrawCall drawCall, HDRConfiguration? hdr = null) {
            var ir = new ImperativeRenderer(
                container, Materials, layer
            );
            ResolveLighting(ref ir, drawCall, hdr);
        }

        /// <summary>
        /// Resolves the current lightmap via the provided renderer.
        /// The provided draw call determines the position and size of the resolved lightmap.
        /// If the provided draw call's texture is not LightingRenderer.Lightmap, it will be modulated by the resolved lightmap.
        /// </summary>
        /// <param name="renderer">The renderer used to resolve the lighting.</param>
        /// <param name="drawCall">A draw call used as a template to resolve the lighting.</param>
        public void ResolveLighting (ref ImperativeRenderer ir, BitmapDrawCall drawCall, HDRConfiguration? hdr = null) {
            if (drawCall.Texture != _Lightmap)
                throw new NotImplementedException("Non-direct resolve not yet implemented");

            Render.EffectMaterial m;
            if (hdr.HasValue && hdr.Value.Mode == HDRMode.GammaCompress)
                m = IlluminantMaterials.GammaCompressedLightingResolve;
            else if (hdr.HasValue && hdr.Value.Mode == HDRMode.ToneMap)
                m = IlluminantMaterials.ToneMappedLightingResolve;
            else
                m = IlluminantMaterials.LightingResolve;

            var sg = ir.MakeSubgroup(before: (dm, _) => {
                var tsize = new Vector2(
                    1f / Configuration.RenderSize.First * Configuration.RenderScale, 
                    1f / Configuration.RenderSize.Second * Configuration.RenderScale
                );
                m.Effect.Parameters["GBufferTexelSize"].SetValue(tsize);
                m.Effect.Parameters["GBuffer"].SetValue(_GBuffer);
                m.Effect.Parameters["RenderScale"].SetValue(Configuration.RenderScale);
                m.Effect.Parameters["InverseScaleFactor"].SetValue(
                    hdr.HasValue
                        ? hdr.Value.InverseScaleFactor
                        : 1.0f
                );

                if (hdr.HasValue) {
                    if (hdr.Value.Mode == HDRMode.GammaCompress)
                        IlluminantMaterials.SetGammaCompressionParameters(
                            hdr.Value.GammaCompression.MiddleGray,
                            hdr.Value.GammaCompression.AverageLuminance,
                            hdr.Value.GammaCompression.MaximumLuminance
                        );
                    else if (hdr.Value.Mode == HDRMode.ToneMap)
                        IlluminantMaterials.SetToneMappingParameters(
                            hdr.Value.ToneMapping.Exposure,
                            hdr.Value.ToneMapping.WhitePoint
                        );
                }
            });

            sg.Draw(drawCall, material: m);
        }        

        private void SetDistanceFieldParameters (EffectParameterCollection p, bool setDistanceTexture) {
            var s = p["DistanceField"].StructureMembers;

            s["Extent"].SetValue(new Vector3(
                Configuration.DistanceFieldSize.First,
                Configuration.DistanceFieldSize.Second,
                Environment.MaximumZ
            ));
            s["TextureSliceSize"].SetValue(new Vector2(1f / DistanceFieldSlicesX, 1f / DistanceFieldSlicesY));
            s["TextureSliceCount"].SetValue(new Vector3(DistanceFieldSlicesX, DistanceFieldSlicesY, _DistanceFieldSlicesReady));

            var tsize = new Vector2(
                1f / (Configuration.DistanceFieldSize.First * DistanceFieldSlicesX), 
                1f / (Configuration.DistanceFieldSize.Second * DistanceFieldSlicesY)
            );
            s["TextureTexelSize"].SetValue(tsize);
            s["InvScaleFactor"].SetValue(1f / Configuration.DistanceFieldResolution);
            s["OcclusionToOpacityPower"].SetValue(Configuration.DistanceFieldOcclusionToOpacityPower);
            s["MaxConeRadius"].SetValue(Configuration.DistanceFieldMaxConeRadius);
            s["ConeGrowthFactor"].SetValue(Configuration.DistanceFieldConeGrowthFactor);

            s["Step"].SetValue(new Vector3(
                (float)Configuration.DistanceFieldMaxStepCount,
                Configuration.DistanceFieldMinStepSize,
                Configuration.DistanceFieldLongStepFactor
            ));

            p["RenderScale"].SetValue(Configuration.RenderScale);

            if (setDistanceTexture)
                p["DistanceFieldTexture"].SetValue(_DistanceField);
        }

        public void InvalidateFields (
            // TODO: Maybe remove this since I'm not sure it's useful at all.
            Bounds3? region = null
        ) {
            _GBufferReady = false;

            for (var i = 0; i < Configuration.DistanceFieldSliceCount; i++) {
                if (_InvalidDistanceFieldSlices.Contains(i))
                    continue;

                _InvalidDistanceFieldSlices.Add(i);
            }
        }

        public void UpdateFields (IBatchContainer container, int layer) {
            if (
                (_DistanceFieldSlicesReady < Configuration.DistanceFieldSliceCount) &&
                (_InvalidDistanceFieldSlices.Count == 0)
            ) {
                InvalidateFields();
            }

            if (!_GBufferReady) {
                RenderGBuffer(ref layer, container);
                _GBufferReady = Configuration.GBufferCaching;
            }

            if (
                (_InvalidDistanceFieldSlices.Count > 0)
            ) {
                RenderDistanceField(ref layer, container);
            }
        }

        private void RenderGBuffer (ref int layerIndex, IBatchContainer resultGroup) {
            using (var group = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex, _GBuffer,
                // FIXME: Optimize this
                (dm, _) => {
                    Materials.PushViewTransform(ViewTransform.CreateOrthographic(
                        (int)(Configuration.RenderSize.First / Configuration.RenderScale), 
                        (int)(Configuration.RenderSize.Second / Configuration.RenderScale)
                    ));
                },
                (dm, _) => {
                    Materials.PopViewTransform();
                }
            )) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, -1, "LightingRenderer {0:X4} : Begin G-Buffer", this.GetHashCode());

                ClearBatch.AddNew(
                    group, 0, Materials.Clear, 
                    Color.Transparent, clearZ: 0
                );

                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 1, IlluminantMaterials.HeightVolume,
                    (dm, _) => {
                        var p = IlluminantMaterials.HeightVolumeFace.Effect.Parameters;
                        p["DistanceFieldExtent"].SetValue(new Vector3(
                            Configuration.DistanceFieldSize.First,
                            Configuration.DistanceFieldSize.Second,
                            Environment.MaximumZ
                        ));
                        p["ZToYMultiplier"].SetValue(
                            Configuration.TwoPointFiveD
                                ? Environment.ZToYMultiplier
                                : 0.0f
                        );
                        p["RenderScale"].SetValue(Configuration.RenderScale);
                        IlluminantMaterials.HeightVolume.Effect.Parameters["RenderScale"].SetValue(
                            Configuration.RenderScale
                        );
                    }
                )) {

                    // HACK: Fill in the gbuffer values for the ground plane
                    {
                        var zRange = new Vector2(Environment.GroundZ, Environment.GroundZ);
                        var indices = new short[] {
                            0, 1, 3, 1, 2, 3
                        };
                        var tl = new Vector3(0, 0, Environment.GroundZ);
                        var tr = new Vector3(Configuration.DistanceFieldSize.First, 0, Environment.GroundZ);
                        var br = new Vector3(Configuration.DistanceFieldSize.First, Configuration.DistanceFieldSize.Second, Environment.GroundZ);
                        var bl = new Vector3(0, Configuration.DistanceFieldSize.Second, Environment.GroundZ);

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
                            PrimitiveType.TriangleList, verts, 0, 4, indices, 0, 2
                        ));
                    }

                    if (Configuration.TwoPointFiveD) {
                        if (Render.Tracing.RenderTrace.EnableTracing) {
                            Render.Tracing.RenderTrace.Marker(group, 2, "LightingRenderer {0:X4} : G-Buffer Top Faces", this.GetHashCode());
                            Render.Tracing.RenderTrace.Marker(group, 4, "LightingRenderer {0:X4} : G-Buffer Front Faces", this.GetHashCode());
                            Render.Tracing.RenderTrace.Marker(group, 6, "LightingRenderer {0:X4} : G-Buffer Billboards", this.GetHashCode());
                        }

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

                        RenderGBufferBillboards(group, 7);

                    } else {
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

                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, 9999, "LightingRenderer {0:X4} : End G-Buffer", this.GetHashCode());
            }
        }

        private void RenderGBufferBillboards (IBatchContainer container, int layerIndex) {
            var indices = new short[] {
                0, 1, 3, 1, 2, 3
            };            
            foreach (var billboard in Environment.Billboards) {
                var tl   = billboard.Position;
                var size = billboard.Size;
                var normal1 = billboard.Normal;
                var normal2 = billboard.Normal;
                var bl = tl + new Vector3(0, size.Y, 0);
                var tr = tl + new Vector3(size.X, 0, 0);

                if (billboard.CylinderNormals) {
                    // FIXME: Linear filtering = not a cylinder?
                    normal1.X = 0;
                    normal2.X = 1;
                }

                var verts = new BillboardVertex[] {
                    new BillboardVertex {
                        Position = tl,
                        Normal = normal1,
                        WorldPosition = bl + new Vector3(0, 0, size.Z),
                        TexCoord = Vector2.Zero,
                        DataScale = billboard.DataScale,
                    },
                    new BillboardVertex {
                        Position = tr,
                        Normal = normal2,
                        WorldPosition = bl + new Vector3(size.X, 0, size.Z),
                        TexCoord = new Vector2(1, 0),
                        DataScale = billboard.DataScale,
                    },
                    new BillboardVertex {
                        Position = tl + size,
                        Normal = normal2,
                        WorldPosition = bl + new Vector3(size.X, 0, 0),
                        TexCoord = Vector2.One,
                        DataScale = billboard.DataScale,
                    },
                    new BillboardVertex {
                        Position = bl,
                        Normal = normal1,
                        WorldPosition = bl,
                        TexCoord = new Vector2(0, 1),
                        DataScale = billboard.DataScale,
                    }
                };

                var material =
                    billboard.Type == BillboardType.Mask
                        ? IlluminantMaterials.MaskBillboard
                        : IlluminantMaterials.GDataBillboard;

                using (var batch = PrimitiveBatch<BillboardVertex>.New(
                    container, layerIndex++, Materials.Get(
                        material,
                        depthStencilState: DepthStencilState.None,
                        rasterizerState: RasterizerState.CullNone,
                        blendState: BlendState.Opaque
                    ), (dm, _) => {
                        var p = material.Effect.Parameters;
                        p["DistanceFieldExtent"].SetValue(new Vector3(
                            Configuration.DistanceFieldSize.First,
                            Configuration.DistanceFieldSize.Second,
                            Environment.MaximumZ
                        ));
                        p["ZToYMultiplier"].SetValue(
                            Configuration.TwoPointFiveD
                                ? Environment.ZToYMultiplier
                                : 0.0f
                        );
                        p["RenderScale"].SetValue(Configuration.RenderScale);
                        p["Mask"].SetValue(billboard.Texture);

                        material.Flush();
                    }
                )) {
                    batch.Add(new PrimitiveDrawCall<BillboardVertex>(
                        PrimitiveType.TriangleList, verts, 0, 4, indices, 0, 2
                    ));
                }
            }
        }

        private void RenderDistanceField (ref int layerIndex, IBatchContainer resultGroup) {
            var indices = new short[] {
                0, 1, 3, 1, 2, 3
            };
            
            using (var rtGroup = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex++, _DistanceField
            )) {
                var intParameters = IlluminantMaterials.DistanceFieldInterior.Effect.Parameters;
                var extParameters = IlluminantMaterials.DistanceFieldExterior.Effect.Parameters;

                // We incrementally do a partial update of the distance field.
                int sliceCount = Configuration.DistanceFieldSliceCount;
                int slicesToUpdate =
                    Math.Min(
                        Configuration.DistanceFieldUpdateRate,
                        _InvalidDistanceFieldSlices.Count
                    );

                int layer = 0;
                while (slicesToUpdate > 0) {
                    var slice = _InvalidDistanceFieldSlices[0];
                    _InvalidDistanceFieldSlices.RemoveAt(0);

                    var physicalSlice = slice / PackedSliceCount;
                    var sliceZ = SliceIndexToZ(slice);

                    // TODO: Render four slices at once by writing to all channels in one go?
                    RenderDistanceFieldSlice(
                        indices, rtGroup,
                        intParameters, extParameters,
                        physicalSlice, slice % PackedSliceCount, sliceZ,
                        ref layer
                    );

                    // Every 'r' channel slice should always be mirrored into the previous slice's 'a' channel
                    // This ensures that we can always access a slice & its neighbor in a single texture load.
                    if (
                        ((slice % PackedSliceCount) == 0) &&
                        (slice > 0)
                    ) {
                        RenderDistanceFieldSlice(
                            indices, rtGroup,
                            intParameters, extParameters,
                            physicalSlice - 1, 3, sliceZ,
                            ref layer
                        );
                    }

                    _DistanceFieldSlicesReady = Math.Min(
                        Math.Max(slice + 1, _DistanceFieldSlicesReady),
                        Configuration.DistanceFieldSliceCount
                    );

                    slicesToUpdate--;
                }
            }
        }

        private float SliceIndexToZ (int slice) {
            float sliceZ = (slice / Math.Max(1, (float)(Configuration.DistanceFieldSliceCount - 1)));
            return sliceZ * Environment.MaximumZ;
        }

        private void RenderDistanceFieldSlice (
            short[] indices, BatchGroup rtGroup, 
            EffectParameterCollection intParameters, EffectParameterCollection extParameters,
            int physicalSliceIndex, int maskIndex, float sliceZ, 
            ref int layer
        ) {
            var sliceX = (physicalSliceIndex % DistanceFieldSlicesX) * DistanceFieldSliceWidth;
            var sliceY = (physicalSliceIndex / DistanceFieldSlicesX) * DistanceFieldSliceHeight;
            var sliceXVirtual = (physicalSliceIndex % DistanceFieldSlicesX) * Configuration.DistanceFieldSize.First;
            var sliceYVirtual = (physicalSliceIndex / DistanceFieldSlicesX) * Configuration.DistanceFieldSize.Second;

            bool forceClear = _DistanceFieldSlicesReady == 0;

            Action<DeviceManager, object> beginSliceBatch =
                (dm, _) => {
                    if (forceClear)
                        dm.Device.Clear(Color.Transparent);

                    // TODO: Optimize this
                    var vt = ViewTransform.CreateOrthographic(
                        Configuration.DistanceFieldSize.First * DistanceFieldSlicesX,
                        Configuration.DistanceFieldSize.Second * DistanceFieldSlicesY
                    );
                    vt.Position = new Vector2(-sliceXVirtual, -sliceYVirtual);
                    Materials.PushViewTransform(ref vt);

                    dm.Device.ScissorRectangle = new Rectangle(
                        sliceX, sliceY, DistanceFieldSliceWidth, DistanceFieldSliceHeight
                    );
                    dm.Device.BlendState = PackedSlice[maskIndex];

                    SetDistanceFieldParameters(IlluminantMaterials.DistanceFieldInterior.Effect.Parameters, false);
                    SetDistanceFieldParameters(IlluminantMaterials.DistanceFieldExterior.Effect.Parameters, false);
                };

            Action<DeviceManager, object> endSliceBatch =
                (dm, _) => {
                    Materials.PopViewTransform();
                };

            var clearBlendState = ClearPackedSlice[maskIndex];

            ClearDistanceFieldSlice(
                indices, clearBlendState,
                beginSliceBatch, endSliceBatch,
                rtGroup, layer++
            );

            using (var group = BatchGroup.New(rtGroup, layer++,
                beginSliceBatch, endSliceBatch
            )) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, -1, "LightingRenderer {0:X4} : Begin Distance Field Slice Z={1} Idx={2}", this.GetHashCode(), sliceZ, physicalSliceIndex);

                RenderDistanceFieldDistanceFunctions(indices, sliceZ, group);
                RenderDistanceFieldHeightVolumes(indices, intParameters, extParameters, sliceZ, group);

                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, 2, "LightingRenderer {0:X4} : End Distance Field Slice Z={1} Idx={2}", this.GetHashCode(), sliceZ, physicalSliceIndex);
            }
        }

        private void RenderDistanceFieldHeightVolumes (short[] indices, EffectParameterCollection intParameters, EffectParameterCollection extParameters, float sliceZ, BatchGroup group) {
            int i = 1;

            // Rasterize the height volumes in sequential order.
            // FIXME: Depth buffer/stencil buffer tricks should work for generating this SDF, but don't?
            using (var interiorGroup = BatchGroup.New(group, 1, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(intParameters, false);
            }))
            using (var exteriorGroup = BatchGroup.New(group, 2, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(extParameters, false);
            }))
            foreach (var hv in Environment.HeightVolumes) {
                var p = hv.Polygon;
                var m = hv.Mesh3D;
                var b = hv.Bounds.Expand(DistanceLimit, DistanceLimit);
                var zRange = new Vector2(hv.ZBase, hv.ZBase + hv.Height);

                var boundingBoxVertices = new HeightVolumeVertex[] {
                    new HeightVolumeVertex(new Vector3(b.TopLeft, 0), Vector3.Up, zRange),
                    new HeightVolumeVertex(new Vector3(b.TopRight, 0), Vector3.Up, zRange),
                    new HeightVolumeVertex(new Vector3(b.BottomRight, 0), Vector3.Up, zRange),
                    new HeightVolumeVertex(new Vector3(b.BottomLeft, 0), Vector3.Up, zRange)
                };

                Texture2D vertexDataTexture;

                // FIXME: Handle position/zrange updates
                if (!HeightVolumeVertexData.TryGetValue(p, out vertexDataTexture)) {
                    lock (Coordinator.CreateResourceLock) {
                        vertexDataTexture = new Texture2D(Coordinator.Device, p.Count, 1, false, SurfaceFormat.HalfVector4);
                        HeightVolumeVertexData[p] = vertexDataTexture;
                    }

                    lock (Coordinator.UseResourceLock)
                    using (var vertices = BufferPool<HalfVector4>.Allocate(p.Count)) {
                        for (var j = 0; j < p.Count; j++) {
                            var edgeA = p[j];
                            var edgeB = p[Arithmetic.Wrap(j + 1, 0, p.Count - 1)];
                            vertices.Data[j] = new HalfVector4(
                                edgeA.X, edgeA.Y, edgeB.X, edgeB.Y
                            );
                        }

                        vertexDataTexture.SetData(vertices.Data, 0, p.Count);
                    }
                }

                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    interiorGroup, i, IlluminantMaterials.DistanceFieldInterior,
                    (dm, _) => {
                        intParameters["NumVertices"].SetValue(p.Count);
                        intParameters["VertexDataTexture"].SetValue(vertexDataTexture);
                        intParameters["SliceZ"].SetValue(sliceZ);
                        IlluminantMaterials.DistanceFieldInterior.Flush();
                    }
                ))
                    batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                        PrimitiveType.TriangleList,
                        m, 0, m.Length / 3
                    ));

                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    exteriorGroup, i, IlluminantMaterials.DistanceFieldExterior,
                    (dm, _) => {
                        extParameters["NumVertices"].SetValue(p.Count);
                        extParameters["VertexDataTexture"].SetValue(vertexDataTexture);
                        extParameters["SliceZ"].SetValue(sliceZ);
                        IlluminantMaterials.DistanceFieldExterior.Flush();
                    }
                ))
                    batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                        PrimitiveType.TriangleList,
                        boundingBoxVertices, 0, boundingBoxVertices.Length, indices, 0, indices.Length / 3
                    ));

                i++;
            }
        }

        private void ClearDistanceFieldSlice (
            short[] indices, 
            BlendState blendState,
            Action<DeviceManager, object> beginBatch,
            Action<DeviceManager, object> endBatch,
            IBatchContainer container, int layer
        ) {
            var verts = new VertexPositionColor[] {
                new VertexPositionColor(new Vector3(0, 0, 0), Color.Transparent),
                new VertexPositionColor(new Vector3(Configuration.DistanceFieldSize.First, 0, 0), Color.Transparent),
                new VertexPositionColor(new Vector3(Configuration.DistanceFieldSize.First, Configuration.DistanceFieldSize.Second, 0), Color.Transparent),
                new VertexPositionColor(new Vector3(0, Configuration.DistanceFieldSize.Second, 0), Color.Transparent)
            };

            // HACK: Create a group to attach begin/end callbacks to
            using (var group = BatchGroup.New(
                container, layer,
                beginBatch, endBatch
            ))
            using (var batch = PrimitiveBatch<VertexPositionColor>.New(
                // FIXME: Only clear the current channel
                group, 0,
                Materials.Get(Materials.WorldSpaceGeometry, blendState: blendState)
            ))
                batch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                    PrimitiveType.TriangleList,
                    verts, 0, 4, indices, 0, 2
                ));
        }

        private void RenderDistanceFieldDistanceFunctions (short[] indices, float sliceZ, BatchGroup group) {
            var verts = new VertexPositionColor[] {
                new VertexPositionColor(new Vector3(0, 0, 0), Color.White),
                new VertexPositionColor(new Vector3(Configuration.DistanceFieldSize.First, 0, 0), Color.White),
                new VertexPositionColor(new Vector3(Configuration.DistanceFieldSize.First, Configuration.DistanceFieldSize.Second, 0), Color.White),
                new VertexPositionColor(new Vector3(0, Configuration.DistanceFieldSize.Second, 0), Color.White)
            };

            var items = Environment.Obstructions;
            var types = new float[items.Count];
            var centers = new Vector3[items.Count];
            var sizes = new Vector3[items.Count];

            for (int i = 0; i < items.Count; i++) {
                var item = items[i];
                types[i] = (int)item.Type;
                centers[i] = item.Center;
                sizes[i] = item.Radius;
            }

            using (var batch = PrimitiveBatch<VertexPositionColor>.New(
                group, 1, IlluminantMaterials.DistanceFunction,
                (dm, _) => {
                    var p = IlluminantMaterials.DistanceFunction.Effect.Parameters;

                    p["NumDistanceObjects"].SetValue(items.Count);
                    p["DistanceObjectTypes"].SetValue(types);
                    p["DistanceObjectCenters"].SetValue(centers);
                    p["DistanceObjectSizes"].SetValue(sizes);
                    p["SliceZ"].SetValue(sliceZ);

                    SetDistanceFieldParameters(p, false);

                    IlluminantMaterials.DistanceFunction.Flush();
                }
            ))
                batch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                    PrimitiveType.TriangleList,
                    verts, 0, 4, indices, 0, 2
                ));
        }
    }

    public class RendererConfiguration {
        // The size of the distance field (x/y/z).
        // Your actual z coordinates are scaled to fit into the z range of the field.
        // If the x/y resolution of the field is too high the z resolution may be reduced.
        public readonly Triplet<int> DistanceFieldSize;

        // The maximum width and height of the viewport.
        public readonly Pair<int>    MaximumRenderSize;

        // Uses a high-precision g-buffer and internal lightmap.
        public readonly bool         HighQuality;
        // Generates downscaled versions of the internal lightmap that the
        //  renderer can use to estimate the brightness of the scene for HDR.
        public readonly bool         EnableBrightnessEstimation;

        // Scales world coordinates when rendering the G-buffer and lightmap
        public float RenderScale                   = 1.0f;

        public bool  TwoPointFiveD                 = false;
        public bool  GBufferCaching                = true;
        public bool  RenderGroundPlane             = true;

        // Individual cone trace steps are not allowed to be any shorter than this.
        // Improves the worst-case performance of the trace and avoids spending forever
        //  stepping short distances around the edges of objects.
        // Setting this to 1 produces the 'best' results but larger values tend to look
        //  just fine. If this is too high you will get banding artifacts.
        public float DistanceFieldMinStepSize             = 3.0f;
        // Long step distances are scaled by this factor. A factor < 1.0
        //  eliminates banding artifacts in the soft area between full/no shadow,
        //  at the cost of additional cone trace steps.
        // This effectively increases how much time we spend outside of objects,
        //  producing higher quality as a side effect.
        // Only set this above 1.0 if you love goofy looking artifacts
        public float DistanceFieldLongStepFactor          = 1.0f;
        // Terminates a cone trace after this many steps.
        // Mitigates the performance hit for complex traces near the edge of objects.
        // Most traces will not hit this cap.
        public int   DistanceFieldMaxStepCount            = 64;
        public float DistanceFieldResolution              = 1.0f;
        public float DistanceFieldMaxConeRadius           = 24;
        public float DistanceFieldConeGrowthFactor        = 1.0f;
        // The maximum number of distance field slices to update per frame.
        // Setting this value too high can crash your video driver.
        public int   DistanceFieldUpdateRate              = 1;
        public float DistanceFieldOcclusionToOpacityPower = 1;

        // The actual number of depth slices allocated for the distance field.
        public int DistanceFieldSliceCount {
            get; internal set;
        }

        // The current width and height of the viewport (and gbuffer).
        // Must not be larger than MaximumRenderSize.
        public Pair<int> RenderSize;        

        public RendererConfiguration (
            int maxWidth, int maxHeight, bool highQuality,
            int distanceFieldWidth, int distanceFieldHeight, int distanceFieldDepth,
            bool enableBrightnessEstimation = false
        ) {
            HighQuality = highQuality;
            MaximumRenderSize = new Pair<int>(maxWidth, maxHeight);
            DistanceFieldSize = new Triplet<int>(distanceFieldWidth, distanceFieldHeight, distanceFieldDepth);
            RenderSize = MaximumRenderSize;
            EnableBrightnessEstimation = enableBrightnessEstimation;
        }
    }

    public struct HDRConfiguration {
        public struct GammaCompressionConfiguration {
            public float MiddleGray, AverageLuminance, MaximumLuminance;
        }

        public struct ToneMappingConfiguration {
            public float Exposure, WhitePoint;
        }

        public HDRMode Mode;
        public float InverseScaleFactor;
        public GammaCompressionConfiguration GammaCompression;
        public ToneMappingConfiguration ToneMapping;
    }

    public enum HDRMode {
        None,
        GammaCompress,
        ToneMap
    }

    public struct LightmapInfo {
        public float Minimum, Maximum, Mean;
        public float Overexposed;
    }
}
