﻿// #define SHADOW_VIZ

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.Illuminant {
    public class RendererConfiguration {
        // The size of the distance field (x/y/z).
        // Your actual z coordinates are scaled to fit into the z range of the field.
        // If the x/y resolution of the field is too high the z resolution may be reduced.
        public readonly Triplet<int> DistanceFieldSize;

        // The maximum width and height of the viewport.
        public readonly Pair<int>    MaximumRenderSize;

        // Scales world coordinates when rendering the G-buffer and lightmap
        public float RenderScale                   = 1.0f;

        public bool  TwoPointFiveD                 = false;
        // If true, 2.5d surfaces are rendered directly to the lightmap, culled via the
        //  depth buffer. Otherwise, they are rendered to the G-buffer.
        public bool  RenderTwoPointFiveDToLightmap = true;

        public bool  GBufferCaching                = true;

        // Individual cone trace steps are not allowed to be any shorter than this.
        // Improves the worst-case performance of the trace and avoids spending forever
        //  stepping short distances around the edges of objects.
        // Setting this to 1 produces the 'best' results but larger values tend to look
        //  just fine. If this is too high you will get banding artifacts.
        public float DistanceFieldMinStepSize             = 3.0f;
        // The minimum step size increases by this much every pixel, so that steps
        //  naturally become longer as the ray gets longer.
        // Making this value too large will introduce banding artifacts.
        public float DistanceFieldMinStepSizeGrowthRate   = 0.01f;
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
        public bool  DistanceFieldCaching                 = true;
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

        public bool RenderTwoPointFiveDToGBuffer {
            get {
                return !RenderTwoPointFiveDToLightmap;
            }
            set {
                RenderTwoPointFiveDToLightmap = !value;
            }
        }

        public RendererConfiguration (
            int maxWidth, int maxHeight,
            int distanceFieldWidth, int distanceFieldHeight, int distanceFieldDepth
        ) {
            MaximumRenderSize = new Pair<int>(maxWidth, maxHeight);
            DistanceFieldSize = new Triplet<int>(distanceFieldWidth, distanceFieldHeight, distanceFieldDepth);
            RenderSize = MaximumRenderSize;
        }
    }

    public class LightingRenderer : IDisposable {
        private class LightSourceComparer : IComparer<LightSource> {
            public int Compare (LightSource lhs, LightSource rhs) {
                int result = ((int)lhs.Mode).CompareTo(((int)rhs.Mode));

                if (result == 0)
                    result = ((int)lhs.RampMode).CompareTo(((int)rhs.RampMode));

                if (result == 0)
                    result = (lhs.ClipRegion.HasValue ? 1 : 0).CompareTo(rhs.ClipRegion.HasValue ? 1 : 0);

                if (result == 0)
                    result = lhs._RampTextureID.CompareTo(rhs._RampTextureID);

                if (result == 0)
                    result = ((int)lhs.RampTextureFilter).CompareTo((int)rhs.RampTextureFilter);

                if (result == 0) {
                    result = lhs.NeutralColor.X.CompareTo(rhs.NeutralColor.X);
                    if (result == 0)
                        result = lhs.NeutralColor.Y.CompareTo(rhs.NeutralColor.Y);
                    if (result == 0)
                        result = lhs.NeutralColor.Z.CompareTo(rhs.NeutralColor.Z);
                    if (result == 0)
                        result = lhs.NeutralColor.W.CompareTo(rhs.NeutralColor.W);
                }

                return result;
            }
        }

        private struct PointLightRecord {
            public int VertexOffset, IndexOffset, VertexCount, IndexCount;
        }

        // HACK: If your projection matrix and your actual viewport/RT don't match in dimensions, you need to set this to compensate. :/
        // Scissor rects are fussy.
        public readonly DefaultMaterialSet Materials;
        public readonly RenderCoordinator Coordinator;
        public readonly IlluminantMaterials IlluminantMaterials;
        public readonly Squared.Render.EffectMaterial[] PointLightMaterialsInner = new Squared.Render.EffectMaterial[4];
        public readonly DepthStencilState TopFaceDepthStencilState, FrontFaceDepthStencilState;
        public readonly DepthStencilState DistanceInteriorStencilState, DistanceExteriorStencilState;
        public readonly DepthStencilState PointLightDepthStencilState;
        public readonly BlendState        OddSlice, EvenSlice;
        public readonly BlendState        ClearOddSlice, ClearEvenSlice;

        private static readonly Dictionary<TextureFilter, SamplerState> RampSamplerStates = new Dictionary<TextureFilter, SamplerState>();

        private readonly LightSourceComparer LightSourceComparerInstance = new LightSourceComparer();

        private PointLightVertex[] PointLightVertices = new PointLightVertex[128];
        private short[] PointLightIndices = null;
        private readonly List<PointLightRecord> PointLightBatchBuffer = new List<PointLightRecord>(128);
        private Rectangle StoredScissorRect;

        private readonly RenderTarget2D _GBuffer;
        private readonly RenderTarget2D _DistanceField;

        private readonly Action<DeviceManager, object> BeginLightPass, EndLightPass, RestoreScissorRect, IlluminationBatchSetup;

        public readonly RendererConfiguration Configuration;
        public LightingEnvironment Environment;

        // HACK
        private int       _DistanceFieldSlicesReady = 0;
        private int       _NextDistanceFieldSlice   = 0;
        private bool      _HeightmapReady     = false;

        const int FaceMaxLights = 16;

        private int       _VisibleLightCount  = 0;
        private Vector3[] _LightPositions     = new Vector3[FaceMaxLights];
        private Vector3[] _LightProperties    = new Vector3[FaceMaxLights];
        private Vector4[] _LightNeutralColors = new Vector4[FaceMaxLights];
        private Vector4[] _LightColors        = new Vector4[FaceMaxLights];

        const int   DistanceLimit = 610;
        const int   StencilTrue  = 0xFF;
        const int   StencilFalse = 0x00;

        const SurfaceFormat GBufferFormat       = SurfaceFormat.Vector4;
        const SurfaceFormat DistanceFieldFormat = SurfaceFormat.Rg32;

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
                _GBuffer = new RenderTarget2D(
                    coordinator.Device, 
                    Configuration.MaximumRenderSize.First, 
                    Configuration.MaximumRenderSize.Second,
                    false, GBufferFormat, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents
                );

                DistanceFieldSliceWidth = (int)(Configuration.DistanceFieldSize.First * Configuration.DistanceFieldResolution);
                DistanceFieldSliceHeight = (int)(Configuration.DistanceFieldSize.Second * Configuration.DistanceFieldResolution);
                int maxSlicesX = 4096 / DistanceFieldSliceWidth;
                int maxSlicesY = 4096 / DistanceFieldSliceHeight;
                // HACK: We encode odd/even slices in the red and green channels
                int maxSlices = maxSlicesX * maxSlicesY * 2;
                
                // HACK: If they ask for too many slices we give them as many as we can.
                int numSlices = Math.Min(Configuration.DistanceFieldSize.Third, maxSlices);
                Configuration.DistanceFieldSliceCount = numSlices;

                int effectiveSliceCount = (numSlices + 1) / 2;

                DistanceFieldSlicesX = Math.Min(maxSlicesX, effectiveSliceCount);
                DistanceFieldSlicesY = Math.Max((int)Math.Ceiling(effectiveSliceCount / (float)maxSlicesX), 1);

                _DistanceField = new RenderTarget2D(
                    coordinator.Device,
                    DistanceFieldSliceWidth * DistanceFieldSlicesX, 
                    DistanceFieldSliceHeight * DistanceFieldSlicesY,
                    false, DistanceFieldFormat, DepthFormat.None, 0, 
                    RenderTargetUsage.PreserveContents
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
            
            PointLightDepthStencilState = new DepthStencilState {
                StencilEnable = false,
                DepthBufferEnable = true,
                DepthBufferFunction = CompareFunction.GreaterEqual,
                DepthBufferWriteEnable = false
            };

            DistanceInteriorStencilState = new DepthStencilState {
                StencilEnable = true,
                StencilPass = StencilOperation.Replace,
                StencilFail = StencilOperation.Keep,
                StencilFunction = CompareFunction.Always,

                ReferenceStencil = StencilTrue,
                StencilMask = StencilTrue,
                StencilWriteMask = StencilTrue,

                DepthBufferEnable = true,
                DepthBufferFunction = CompareFunction.LessEqual,
                DepthBufferWriteEnable = true
            };

            DistanceExteriorStencilState = new DepthStencilState {
                StencilEnable = true,
                StencilFunction = CompareFunction.Equal,

                ReferenceStencil = StencilFalse,
                StencilMask = StencilTrue,
                StencilWriteMask = StencilFalse,

                DepthBufferEnable = true,
                DepthBufferFunction = CompareFunction.LessEqual,
                DepthBufferWriteEnable = true,
            };

            EvenSlice = new BlendState {
                ColorWriteChannels = ColorWriteChannels.Red,
                ColorBlendFunction = BlendFunction.Max,
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One
            };

            OddSlice = new BlendState {
                ColorWriteChannels = ColorWriteChannels.Green,
                ColorBlendFunction = BlendFunction.Max,
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.One
            };

            ClearEvenSlice = new BlendState {
                ColorWriteChannels = ColorWriteChannels.Red,
                ColorBlendFunction = BlendFunction.Add,
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.Zero
            };

            ClearOddSlice = new BlendState {
                ColorWriteChannels = ColorWriteChannels.Green,
                ColorBlendFunction = BlendFunction.Add,
                ColorSourceBlend = Blend.One,
                ColorDestinationBlend = Blend.Zero
            };

            {
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RenderStates.ScissorOnly, 
                        depthStencilState: PointLightDepthStencilState
                    )
                };
                Action<DeviceManager>[] dEnd = null;

                materials.Add(IlluminantMaterials.PointLightExponential = new DelegateMaterial(
                    PointLightMaterialsInner[0] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("Illumination"), "PointLightExponential"
                    ), dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.PointLightLinear = new DelegateMaterial(
                    PointLightMaterialsInner[1] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("Illumination"), "PointLightLinear"
                    ), dBegin, dEnd
                ));

#if !SDL2
                materials.Add(IlluminantMaterials.PointLightExponentialRampTexture = new DelegateMaterial(
                    PointLightMaterialsInner[2] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("Illumination"), "PointLightExponentialRampTexture"
                    ), dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.PointLightLinearRampTexture = new DelegateMaterial(
                    PointLightMaterialsInner[3] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("Illumination"), "PointLightLinearRampTexture"
                    ), dBegin, dEnd
                ));
#endif

                materials.Add(IlluminantMaterials.VolumeTopFace = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("VolumeFaces"), "VolumeTopFace"));

                materials.Add(IlluminantMaterials.VolumeFrontFace = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("VolumeFaces"), "VolumeFrontFace"));

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
            }

#if SDL2
            materials.Add(IlluminantMaterials.ScreenSpaceGammaCompressedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("ScreenSpaceGammaCompressedBitmap"), "ScreenSpaceGammaCompressedBitmap"
            ));

            materials.Add(IlluminantMaterials.WorldSpaceGammaCompressedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("WorldSpaceGammaCompressedBitmap"), "WorldSpaceGammaCompressedBitmap"
            ));

            materials.Add(IlluminantMaterials.ScreenSpaceToneMappedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("ScreenSpaceToneMappedBitmap"), "ScreenSpaceToneMappedBitmap"
            ));

            materials.Add(IlluminantMaterials.WorldSpaceToneMappedBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("WorldSpaceToneMappedBitmap"), "WorldSpaceToneMappedBitmap"
            ));
#else
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

            materials.Add(IlluminantMaterials.ScreenSpaceRampBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("RampBitmap"), "ScreenSpaceRampBitmap"
            ));

            materials.Add(IlluminantMaterials.WorldSpaceRampBitmap = new Squared.Render.EffectMaterial(
                content.Load<Effect>("RampBitmap"), "WorldSpaceRampBitmap"
            ));
#endif

            Environment = environment;
        }

        public void Dispose () {
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
            device.PushStates();
        }

        private void _EndLightPass (DeviceManager device, object userData) {
            device.PopStates();
        }
        
        internal static SamplerState GetRampSamplerState (TextureFilter filter) {
            SamplerState ss;
            lock (RampSamplerStates) {
                if (!RampSamplerStates.TryGetValue(filter, out ss))
                    RampSamplerStates[filter] = ss = new SamplerState {
                        Filter = filter,
                        AddressU = TextureAddressMode.Clamp,
                        AddressV = TextureAddressMode.Clamp,
                        AddressW = TextureAddressMode.Clamp
                    };
            }

            return ss;
        }

        private void _IlluminationBatchSetup (DeviceManager device, object lightSource) {
            var ls = (LightSource)lightSource;

            switch (ls.Mode) {
                case LightSourceMode.Additive:
                    device.Device.BlendState = RenderStates.AdditiveBlend;
                    break;
                case LightSourceMode.Subtractive:
                    device.Device.BlendState = RenderStates.SubtractiveBlend;
                    break;
                case LightSourceMode.Alpha:
                    device.Device.BlendState = BlendState.AlphaBlend;
                    break;
                case LightSourceMode.Max:
                    device.Device.BlendState = RenderStates.MaxBlend;
                    break;
                case LightSourceMode.Min:
                    device.Device.BlendState = RenderStates.MinBlend;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Mode");
            }

            foreach (var mi in PointLightMaterialsInner) {
                mi.Effect.Parameters["LightNeutralColor"].SetValue(ls.NeutralColor);
#if SDL2
                // Only the RampTexture techniques have this parameter -flibit
                if (mi.Effect.Parameters["RampTexture"] != null)
#endif
                mi.Effect.Parameters["RampTexture"].SetValue(ls.RampTexture);

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

            device.Device.SamplerStates[1] = GetRampSamplerState(ls.RampTextureFilter);
        }

        PointLightVertex MakePointLightVertex (LightSource lightSource, float intensityScale) {
            var vertex = new PointLightVertex();
            vertex.LightCenter = lightSource.Position;
            vertex.Color = lightSource.Color;
            vertex.Color.W *= (lightSource.Opacity * intensityScale);
            vertex.Ramp = new Vector2(lightSource.Radius, lightSource.RampLength);
            return vertex;
        }

        /// <summary>
        /// Renders all light sources into the target batch container on the specified layer.
        /// </summary>
        /// <param name="frame">Necessary for bookkeeping.</param>
        /// <param name="container">The batch container to render lighting into.</param>
        /// <param name="layer">The layer to render lighting into.</param>
        /// <param name="intensityScale">A factor to scale the intensity of all light sources. You can use this to rescale the intensity of light values for HDR.</param>
        public void RenderLighting (Frame frame, IBatchContainer container, int layer, float intensityScale = 1.0f) {
            // FIXME
            var pointLightVertexCount = Environment.LightSources.Count * 4;
            var pointLightIndexCount = Environment.LightSources.Count * 6;
            if (PointLightVertices.Length < pointLightVertexCount)
                PointLightVertices = new PointLightVertex[1 << (int)Math.Ceiling(Math.Log(pointLightVertexCount, 2))];

            if ((PointLightIndices == null) || (PointLightIndices.Length < pointLightIndexCount)) {
                PointLightIndices = new short[pointLightIndexCount];

                int i = 0, j = 0;
                while (i < pointLightIndexCount) {
                    PointLightIndices[i++] = (short)(j + 0);
                    PointLightIndices[i++] = (short)(j + 1);
                    PointLightIndices[i++] = (short)(j + 3);
                    PointLightIndices[i++] = (short)(j + 1);
                    PointLightIndices[i++] = (short)(j + 2);
                    PointLightIndices[i++] = (short)(j + 3);

                    j += 4;
                }
            }

            int vertexOffset = 0, indexOffset = 0;
            LightSource batchFirstLightSource = null;
            BatchGroup currentLightGroup = null;

            int layerIndex = 0;

            using (var sortedLights = BufferPool<LightSource>.Allocate(Environment.LightSources.Count))
            using (var resultGroup = BatchGroup.New(container, layer, before: BeginLightPass, after: EndLightPass)) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(resultGroup, -9999, "Frame {0:0000} : LightingRenderer {1:X4} : Begin", frame.Index, this.GetHashCode());

                if (Configuration.TwoPointFiveD && Configuration.RenderTwoPointFiveDToLightmap) {
                    if (Render.Tracing.RenderTrace.EnableTracing)
                        Render.Tracing.RenderTrace.Marker(resultGroup, layerIndex++, "Frame {0:0000} : LightingRenderer {1:X4} : Lightmap 2.5D", frame.Index, this.GetHashCode());

                    RenderTwoPointFiveDLitSurfaces(ref layerIndex, resultGroup);
                }

                int i = 0;
                var lightCount = 0;

                foreach (var lightSource in Environment.LightSources) {
                    sortedLights.Data[i++] = lightSource;
                    lightCount += 1;
                }

                Array.Sort(sortedLights.Data, 0, lightCount, LightSourceComparerInstance);

                int lightGroupIndex = 1;

                for (i = 0; i < lightCount; i++) {
                    var lightSource = sortedLights.Data[i];

                    if (lightSource.Opacity <= 0)
                        continue;

                    float radius = lightSource.Radius + lightSource.RampLength;
                    var lightBounds = new Bounds(
                        (Vector2)lightSource.Position - new Vector2(radius), (Vector2)lightSource.Position + new Vector2(radius)
                    );

                    if (Configuration.TwoPointFiveD) {
                        lightBounds.TopLeft.Y -= (Environment.MaximumZ * Environment.ZToYMultiplier);
                    }

                    lightBounds = lightBounds.Scale(Configuration.RenderScale);

                    // FIXME: Broken :(
                    if (false) {
                        bool lightWithinVolume = false;

                        // If the light is contained within a height volume that encompasses it in all 3 dimensions, cull it
                        using (var e = Environment.HeightVolumes.GetItemsFromBounds(lightBounds))
                        while (e.MoveNext()) {
                            if (
                                e.Current.Item.IsObstruction && 
                                (e.Current.Item.Height > lightSource.Position.Z) &&
                                Geometry.PointInPolygon((Vector2)lightSource.Position, e.Current.Item.Polygon)
                            ) {
                                lightWithinVolume = true;
                                break;
                            }
                        }

                        if (lightWithinVolume)
                            continue;
                    }

                    if (batchFirstLightSource != null) {
                        var needFlush =
                            (batchFirstLightSource.ClipRegion.HasValue != lightSource.ClipRegion.HasValue) ||
                            (batchFirstLightSource.NeutralColor != lightSource.NeutralColor) ||
                            (batchFirstLightSource.Mode != lightSource.Mode) ||
                            (batchFirstLightSource.RampMode != lightSource.RampMode) ||
                            (batchFirstLightSource.RampTexture != lightSource.RampTexture) ||
                            (batchFirstLightSource.RampTextureFilter != lightSource.RampTextureFilter);

                        if (needFlush) {
                            if (Render.Tracing.RenderTrace.EnableTracing)
                                Render.Tracing.RenderTrace.Marker(currentLightGroup, layerIndex++, "Frame {0:0000} : LightingRenderer {1:X4} : Point Light Flush ({2} point(s))", frame.Index, this.GetHashCode(), PointLightBatchBuffer.Count);
                            FlushPointLightBatch(ref currentLightGroup, ref batchFirstLightSource, ref layerIndex);
                            indexOffset = 0;
                        }
                    }

                    if (batchFirstLightSource == null)
                        batchFirstLightSource = lightSource;

                    if (currentLightGroup == null)
                        currentLightGroup = BatchGroup.New(resultGroup, lightGroupIndex++);

                    Bounds clippedLightBounds;
                    if (lightSource.ClipRegion.HasValue) {
                        var clipBounds = lightSource.ClipRegion.Value;
                        if (!lightBounds.Intersection(ref lightBounds, ref clipBounds, out clippedLightBounds))
                            continue;
                    } else {
                        clippedLightBounds = lightBounds;
                    }

                    var vertex = MakePointLightVertex(lightSource, intensityScale);

                    vertex.Position = clippedLightBounds.TopLeft;
                    PointLightVertices[vertexOffset++] = vertex;

                    vertex.Position = clippedLightBounds.TopRight;
                    PointLightVertices[vertexOffset++] = vertex;

                    vertex.Position = clippedLightBounds.BottomRight;
                    PointLightVertices[vertexOffset++] = vertex;

                    vertex.Position = clippedLightBounds.BottomLeft;
                    PointLightVertices[vertexOffset++] = vertex;

                    var newRecord = new PointLightRecord {
                        VertexOffset = vertexOffset - 4,
                        IndexOffset = indexOffset,
                        VertexCount = 4,
                        IndexCount = 6
                    };

                    if (PointLightBatchBuffer.Count > 0) {
                        var oldRecord = PointLightBatchBuffer[PointLightBatchBuffer.Count - 1];

                        if (
                            (newRecord.VertexOffset == oldRecord.VertexOffset + oldRecord.VertexCount) &&
                            (newRecord.IndexOffset == oldRecord.IndexOffset + oldRecord.IndexCount)
                        ) {
                            oldRecord.VertexCount += newRecord.VertexCount;
                            oldRecord.IndexCount += newRecord.IndexCount;
                            PointLightBatchBuffer[PointLightBatchBuffer.Count - 1] = oldRecord;
                        } else {
                            PointLightBatchBuffer.Add(newRecord);
                        }
                    } else {
                        PointLightBatchBuffer.Add(newRecord);
                    }

                    indexOffset += 6;
                }

                if (PointLightBatchBuffer.Count > 0) {
                    if (Render.Tracing.RenderTrace.EnableTracing)
                        Render.Tracing.RenderTrace.Marker(currentLightGroup, layerIndex++, "Frame {0:0000} : LightingRenderer {1:X4} : Point Light Flush ({2} point(s))", frame.Index, this.GetHashCode(), PointLightBatchBuffer.Count);

                    FlushPointLightBatch(ref currentLightGroup, ref batchFirstLightSource, ref layerIndex);
                }

                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(resultGroup, 9999, "Frame {0:0000} : LightingRenderer {1:X4} : End", frame.Index, this.GetHashCode());
            }
        }        

        private void SetTwoPointFiveDParametersInner (EffectParameterCollection p, bool setGBufferTexture, bool setDistanceTexture) {
            p["GroundZ"]           .SetValue(Environment.GroundZ);
            p["ZToYMultiplier"]    .SetValue(
                Configuration.TwoPointFiveD
                    ? Environment.ZToYMultiplier
                    : 0.0f
            );
            p["LightPositions"]    .SetValue(_LightPositions);
            p["LightProperties"]   .SetValue(_LightProperties);
            p["LightNeutralColors"].SetValue(_LightNeutralColors);
            p["LightColors"]       .SetValue(_LightColors);
            p["NumLights"]         .SetValue(_VisibleLightCount);

            var tsize = new Vector2(
                1f / Configuration.RenderSize.First, 
                1f / Configuration.RenderSize.Second
            );
            p["GBufferInvScaleFactor"].SetValue(1f);
            p["GBufferTexelSize"].SetValue(tsize);

            if (setGBufferTexture)
                p["GBuffer"].SetValue(GBuffer);

            SetDistanceFieldParameters(p, setDistanceTexture);
        }

        private void SetDistanceFieldParameters (EffectParameterCollection p, bool setDistanceTexture) {
            p["DistanceFieldExtent"].SetValue(new Vector3(
                Configuration.DistanceFieldSize.First,
                Configuration.DistanceFieldSize.Second,
                Environment.MaximumZ
            ));
            p["DistanceFieldTextureSliceSize"].SetValue(new Vector2(1f / DistanceFieldSlicesX, 1f / DistanceFieldSlicesY));
            p["DistanceFieldTextureSliceCount"].SetValue(new Vector3(DistanceFieldSlicesX, DistanceFieldSlicesY, _DistanceFieldSlicesReady));

            var tsize = new Vector2(
                1f / (Configuration.DistanceFieldSize.First * DistanceFieldSlicesX), 
                1f / (Configuration.DistanceFieldSize.Second * DistanceFieldSlicesY)
            );
            p["DistanceFieldTextureTexelSize"].SetValue(tsize);
            p["DistanceFieldInvScaleFactor"].SetValue(1f / Configuration.DistanceFieldResolution);
            p["DistanceFieldMinimumStepSize"].SetValue(Configuration.DistanceFieldMinStepSize);
            p["DistanceFieldMinimumStepSizeGrowthRate"].SetValue(Configuration.DistanceFieldMinStepSizeGrowthRate);
            p["DistanceFieldLongStepFactor"].SetValue(Configuration.DistanceFieldLongStepFactor);
            p["DistanceFieldOcclusionToOpacityPower"].SetValue(Configuration.DistanceFieldOcclusionToOpacityPower);
            p["DistanceFieldMaxConeRadius"].SetValue(Configuration.DistanceFieldMaxConeRadius);
            p["DistanceFieldMaxStepCount"].SetValue((float)Configuration.DistanceFieldMaxStepCount);

            if (setDistanceTexture)
                p["DistanceFieldTexture"].SetValue(_DistanceField);
        }

        private void SetTwoPointFiveDParameters (DeviceManager dm, object _) {
            var frontFaceMaterial = IlluminantMaterials.VolumeFrontFace;
            var topFaceMaterial   = IlluminantMaterials.VolumeTopFace;

            SetTwoPointFiveDParametersInner(frontFaceMaterial.Effect.Parameters, true, true);
            SetTwoPointFiveDParametersInner(topFaceMaterial  .Effect.Parameters, true, true);
        }

        private void RenderTwoPointFiveDLitSurfaces (ref int layerIndex, BatchGroup resultGroup) {
            // FIXME: Support more than 12 lights

            int i = 0;
            foreach (var ls in Environment.LightSources) {
                if (i >= FaceMaxLights)
                    break;

                _LightPositions[i]     = ls.Position;
                _LightNeutralColors[i] = ls.NeutralColor;
                _LightColors[i]        = ls.Color;
                _LightColors[i].W     *= ls.Opacity;
                _LightProperties[i]    = new Vector3(
                    ls.Radius, ls.RampLength,
                    (ls.RampMode == LightSourceRampMode.Exponential)
                        ? 1
                        : 0
                );

                i += 1;
            }

            _VisibleLightCount = i;

            for (; i < FaceMaxLights; i++) {
                _LightPositions[i] = _LightProperties[i] = Vector3.Zero;
                _LightColors[i] = _LightNeutralColors[i] = Vector4.Zero;                
            }

            using (var group = BatchGroup.New(
                resultGroup, layerIndex++,
                before: (dm, _) => {
                    dm.PushStates();
                    var vt = Materials.ViewTransform;
                    // FIXME: This seems backwards, shouldn't it be 1.0 / Configuration.RenderScale?
                    vt.Scale = new Vector2(Configuration.RenderScale);
                    Materials.PushViewTransform(ref vt);
                },
                after: (dm, _) => {
                    dm.PopStates();
                    Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(
                    group, 0, Materials.Clear, clearZ: 0f
                );

                using (var topBatch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 1, Materials.Get(
                        IlluminantMaterials.VolumeTopFace,                    
                        depthStencilState: TopFaceDepthStencilState,
                        rasterizerState: RasterizerState.CullNone,
                        blendState: BlendState.Opaque
                    ),
                    batchSetup: SetTwoPointFiveDParameters
                ))
                using (var frontBatch = PrimitiveBatch<HeightVolumeVertex>.New(
                    group, 2, Materials.Get(
                        IlluminantMaterials.VolumeFrontFace,
                        depthStencilState: FrontFaceDepthStencilState,
                        rasterizerState: RasterizerState.CullNone,
                        blendState: BlendState.Opaque
                    ),
                    batchSetup: SetTwoPointFiveDParameters
                )) {
                    foreach (var volume in Environment.HeightVolumes.OrderByDescending(hv => hv.ZBase + hv.Height)) {
                        var ffm3d = volume.GetFrontFaceMesh3D();
                        if (ffm3d.Count <= 0)
                            continue;

                        frontBatch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                            PrimitiveType.TriangleList,
                            ffm3d.Array, ffm3d.Offset, ffm3d.Count / 3
                        ));

                        var m3d = volume.Mesh3D;

                        topBatch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                            PrimitiveType.TriangleList,
                            m3d, 0, m3d.Length / 3
                        ));
                    }
                }
            }
        }

        public void InvalidateFields () {
            _HeightmapReady = false;
            _NextDistanceFieldSlice = _DistanceFieldSlicesReady = 0;
        }

        public void UpdateFields (IBatchContainer container, int layer) {
            if (!_HeightmapReady) {
                RenderGBuffer(ref layer, container);
                _HeightmapReady = Configuration.GBufferCaching;
            }

            if (
                (_DistanceFieldSlicesReady < Configuration.DistanceFieldSliceCount) ||
                !Configuration.DistanceFieldCaching
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
                        dm.Device.RasterizerState = RasterizerState.CullNone;
                        // TODO: Depth buffer?
                        dm.Device.DepthStencilState = DepthStencilState.None;
                        dm.Device.BlendState = BlendState.Opaque;

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
                    }
                )) {

                    // HACK: Fill in the gbuffer values for the ground plane
                    {
                        var zRange = new Vector2(Environment.GroundZ, Environment.GroundZ);
                        var indices = new short[] {
                            0, 1, 3, 1, 2, 3
                        };            
                        var verts = new HeightVolumeVertex[] {
                            new HeightVolumeVertex(new Vector3(0, 0, Environment.GroundZ), Vector3.Up, zRange),
                            new HeightVolumeVertex(new Vector3(Configuration.DistanceFieldSize.First, 0, Environment.GroundZ), Vector3.Up, zRange),
                            new HeightVolumeVertex(new Vector3(Configuration.DistanceFieldSize.First, Configuration.DistanceFieldSize.Second, Environment.GroundZ), Vector3.Up, zRange),
                            new HeightVolumeVertex(new Vector3(0, Configuration.DistanceFieldSize.Second, Environment.GroundZ), Vector3.Up, zRange)
                        };

                        batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                            PrimitiveType.TriangleList, verts, 0, 4, indices, 0, 2
                        ));
                    }

                    if (Configuration.TwoPointFiveD && Configuration.RenderTwoPointFiveDToGBuffer) {
                        if (Render.Tracing.RenderTrace.EnableTracing)
                            Render.Tracing.RenderTrace.Marker(group, 2, "LightingRenderer {0:X4} : G-Buffer Top Faces", this.GetHashCode());

                        if (Render.Tracing.RenderTrace.EnableTracing)
                            Render.Tracing.RenderTrace.Marker(group, 4, "LightingRenderer {0:X4} : G-Buffer Front Faces", this.GetHashCode());

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
                        foreach (var volume in Environment.HeightVolumes.OrderByDescending(hv => hv.ZBase + hv.Height)) {
                            var ffm3d = volume.GetFrontFaceMesh3D();
                            if (ffm3d.Count <= 0)
                                continue;

                            frontBatch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                                PrimitiveType.TriangleList,
                                ffm3d.Array, ffm3d.Offset, ffm3d.Count / 3
                            ));

                            var m3d = volume.Mesh3D;

                            topBatch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                                PrimitiveType.TriangleList,
                                m3d, 0, m3d.Length / 3
                            ));
                        }

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
                    Render.Tracing.RenderTrace.Marker(group, 4, "LightingRenderer {0:X4} : End G-Buffer", this.GetHashCode());
            }
        }

        private void RenderDistanceField (ref int layerIndex, IBatchContainer resultGroup) {
            var indices = new short[] {
                0, 1, 3, 1, 2, 3
            };
            
            using (var rtGroup = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex++, _DistanceField
            )) {
                var vertexDataTextures = new Dictionary<object, Texture2D>();
                var intParameters = IlluminantMaterials.DistanceFieldInterior.Effect.Parameters;
                var extParameters = IlluminantMaterials.DistanceFieldExterior.Effect.Parameters;

                // We incrementally do a partial update of the distance field.
                int sliceCount = Configuration.DistanceFieldSliceCount;
                int slicesToUpdate =
                    Configuration.DistanceFieldCaching
                        ? Math.Min(
                            sliceCount - _DistanceFieldSlicesReady,
                            Configuration.DistanceFieldUpdateRate
                        )
                        : Math.Min(
                            Configuration.DistanceFieldUpdateRate,
                            sliceCount
                        );

                int layer = 0;
                while (slicesToUpdate > 0) {
                    RenderDistanceFieldSlice(
                        indices, rtGroup, vertexDataTextures, 
                        intParameters, extParameters, 
                        _NextDistanceFieldSlice, ref layer
                    );

                    slicesToUpdate--;
                    _NextDistanceFieldSlice = (_NextDistanceFieldSlice + 1) % sliceCount;
                }

                foreach (var kvp in vertexDataTextures)
                    Coordinator.DisposeResource(kvp.Value);
            }
        }

        private void RenderDistanceFieldSlice (
            short[] indices, BatchGroup rtGroup, 
            Dictionary<object, Texture2D> vertexDataTextures, 
            EffectParameterCollection intParameters, EffectParameterCollection extParameters, 
            int slice, ref int layer
        ) {
            // TODO: Duplicate slice data across channels for one-sample reads?

            float sliceZ = (slice / Math.Max(1, (float)(Configuration.DistanceFieldSliceCount - 1))) * Environment.MaximumZ;
            int displaySlice = slice / 2;
            var sliceX = (displaySlice % DistanceFieldSlicesX) * DistanceFieldSliceWidth;
            var sliceY = (displaySlice / DistanceFieldSlicesX) * DistanceFieldSliceHeight;
            var sliceXVirtual = (displaySlice % DistanceFieldSlicesX) * Configuration.DistanceFieldSize.First;
            var sliceYVirtual = (displaySlice / DistanceFieldSlicesX) * Configuration.DistanceFieldSize.Second;

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
                    dm.Device.BlendState = ((slice % 2) == 0)
                        ? EvenSlice
                        : OddSlice;

                    SetDistanceFieldParameters(IlluminantMaterials.DistanceFieldInterior.Effect.Parameters, false);
                    SetDistanceFieldParameters(IlluminantMaterials.DistanceFieldExterior.Effect.Parameters, false);
                };

            Action<DeviceManager, object> endSliceBatch =
                (dm, _) => {
                    Materials.PopViewTransform();
                };

            var clearBlendState = ((slice % 2) == 0)
                ? ClearEvenSlice
                : ClearOddSlice;

            ClearDistanceFieldSlice(
                indices, clearBlendState,
                beginSliceBatch, endSliceBatch,
                rtGroup, layer++
            );

            using (var group = BatchGroup.New(rtGroup, layer++,
                beginSliceBatch, endSliceBatch
            )) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, -1, "LightingRenderer {0:X4} : Begin Distance Field Slice #{1}", this.GetHashCode(), slice);

                RenderDistanceFieldDistanceFunctions(indices, sliceZ, group);
                RenderDistanceFieldHeightVolumes(indices, vertexDataTextures, intParameters, extParameters, sliceZ, group);

                _DistanceFieldSlicesReady = Math.Min(
                    Math.Max(slice + 1, _DistanceFieldSlicesReady),
                    Configuration.DistanceFieldSliceCount
                );

                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, 2, "LightingRenderer {0:X4} : End Distance Field Slice #{1}", this.GetHashCode(), slice);
            }
        }

        private void RenderDistanceFieldHeightVolumes (short[] indices, Dictionary<object, Texture2D> vertexDataTextures, EffectParameterCollection intParameters, EffectParameterCollection extParameters, float sliceZ, BatchGroup group) {
            int i = 1;

            // Rasterize the height volumes in sequential order.
            // FIXME: Depth buffer/stencil buffer tricks should work for generating this SDF, but don't?
            using (var interiorGroup = BatchGroup.ForRenderTarget(group, 1, _DistanceField, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(intParameters, false);
            }))
            using (var exteriorGroup = BatchGroup.ForRenderTarget(group, 2, _DistanceField, (dm, _) => {
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

                    if (!vertexDataTextures.TryGetValue(p, out vertexDataTexture))
                        lock (Coordinator.CreateResourceLock) {
                            vertexDataTexture = new Texture2D(Coordinator.Device, p.Count, 1, false, SurfaceFormat.Vector2);
                            vertexDataTextures[p] = vertexDataTexture;
                        }

                    vertexDataTexture.SetData(p.GetVertices());

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
                sizes[i] = item.Size;
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

        private void FlushPointLightBatch (ref BatchGroup lightGroup, ref LightSource batchFirstLightSource, ref int layerIndex) {
            if (lightGroup == null)
                return;

            Material material;
            if (batchFirstLightSource.RampTexture != null) {
                material = batchFirstLightSource.RampMode == LightSourceRampMode.Linear
                    ? IlluminantMaterials.PointLightLinearRampTexture
                    : IlluminantMaterials.PointLightExponentialRampTexture;
            } else {
                material = batchFirstLightSource.RampMode == LightSourceRampMode.Linear
                    ? IlluminantMaterials.PointLightLinear
                    : IlluminantMaterials.PointLightExponential;
            }

            using (var pb = PrimitiveBatch<PointLightVertex>.New(lightGroup, layerIndex++, material, IlluminationBatchSetup, batchFirstLightSource)) {
                foreach (var record in PointLightBatchBuffer) {
                    var pointLightDrawCall = new PrimitiveDrawCall<PointLightVertex>(
                        PrimitiveType.TriangleList, PointLightVertices, record.VertexOffset, record.VertexCount, PointLightIndices, record.IndexOffset, record.IndexCount / 3
                    );
                    pb.Add(pointLightDrawCall);
                }
            }

            lightGroup.Dispose();
            lightGroup = null;
            batchFirstLightSource = null;
            PointLightBatchBuffer.Clear();
        }
    }

    public class IlluminantMaterials {
        public readonly DefaultMaterialSet MaterialSet;

        public Material PointLightLinear, PointLightExponential, PointLightLinearRampTexture, PointLightExponentialRampTexture;
        public Squared.Render.EffectMaterial VolumeFrontFace, VolumeTopFace;
        public Squared.Render.EffectMaterial DistanceFieldExterior, DistanceFieldInterior;
        public Squared.Render.EffectMaterial DistanceFunction;
        public Squared.Render.EffectMaterial HeightVolume, HeightVolumeFace;
        public Squared.Render.EffectMaterial ScreenSpaceGammaCompressedBitmap, WorldSpaceGammaCompressedBitmap;
        public Squared.Render.EffectMaterial ScreenSpaceToneMappedBitmap, WorldSpaceToneMappedBitmap;
#if !SDL2
        public Squared.Render.EffectMaterial ScreenSpaceRampBitmap, WorldSpaceRampBitmap;
#endif

        internal readonly Effect[] EffectsToSetGammaCompressionParametersOn;
        internal readonly Effect[] EffectsToSetToneMappingParametersOn;

        internal IlluminantMaterials (DefaultMaterialSet materialSet) {
            MaterialSet = materialSet;

            EffectsToSetGammaCompressionParametersOn = new Effect[2];
            EffectsToSetToneMappingParametersOn = new Effect[2];
        }

        /// <summary>
        /// Updates the gamma compression parameters for the gamma compressed bitmap materials. You should call this in batch setup when using the materials.
        /// </summary>
        /// <param name="inverseScaleFactor">If you scaled down the intensity of your light sources for HDR rendering, use this to invert the scale. All other parameters are applied to the resulting scaled value.</param>
        /// <param name="middleGray">I don't know what this does. Impossible to find a paper that actually describes this formula. :/ Try 0.6.</param>
        /// <param name="averageLuminance">The average luminance of the entire scene. You can compute this by scaling the entire scene down or using light receivers.</param>
        /// <param name="maximumLuminance">The maximum luminance. Luminance values above this threshold will remain above 1.0 after gamma compression.</param>
        public void SetGammaCompressionParameters (float inverseScaleFactor, float middleGray, float averageLuminance, float maximumLuminance) {
            const float min = 1 / 256f;
            const float max = 99999f;

            middleGray = MathHelper.Clamp(middleGray, 0.0f, max);
            averageLuminance = MathHelper.Clamp(averageLuminance, min, max);
            maximumLuminance = MathHelper.Clamp(maximumLuminance, min, max);

            EffectsToSetGammaCompressionParametersOn[0] = ScreenSpaceGammaCompressedBitmap.Effect;
            EffectsToSetGammaCompressionParametersOn[1] = WorldSpaceGammaCompressedBitmap.Effect;

            foreach (var effect in EffectsToSetGammaCompressionParametersOn) {
                effect.Parameters["InverseScaleFactor"].SetValue(inverseScaleFactor);
                effect.Parameters["MiddleGray"].SetValue(middleGray);
                effect.Parameters["AverageLuminance"].SetValue(averageLuminance);
                effect.Parameters["MaximumLuminanceSquared"].SetValue(maximumLuminance * maximumLuminance);
            }
        }

        /// <summary>
        /// Updates the tone mapping parameters for the tone mapped bitmap materials. You should call this in batch setup when using the materials.
        /// </summary>
        /// <param name="inverseScaleFactor">If you scaled down the intensity of your light sources for HDR rendering, use this to invert the scale. All other parameters are applied to the resulting scaled value.</param>
        /// <param name="exposure">A factor to multiply incoming values to make them brighter or darker.</param>
        /// <param name="whitePoint">The white point to set as the threshold above which any values become 1.0.</param>
        public void SetToneMappingParameters (float inverseScaleFactor, float exposure, float whitePoint) {
            const float min = 1 / 256f;
            const float max = 99999f;

            exposure = MathHelper.Clamp(exposure, min, max);
            whitePoint = MathHelper.Clamp(whitePoint, min, max);

            EffectsToSetToneMappingParametersOn[0] = ScreenSpaceToneMappedBitmap.Effect;
            EffectsToSetToneMappingParametersOn[1] = WorldSpaceToneMappedBitmap.Effect;

            foreach (var effect in EffectsToSetToneMappingParametersOn) {
                effect.Parameters["InverseScaleFactor"].SetValue(inverseScaleFactor);
                effect.Parameters["Exposure"].SetValue(exposure);
                effect.Parameters["WhitePoint"].SetValue(whitePoint);
            }
        }
    }
}
