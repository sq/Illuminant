// #define SHADOW_VIZ

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
using Squared.Render.Tracing;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private struct LightTypeRenderStateKey {
            public LightSourceTypeID Type;
            public Texture2D         RampTexture;
            public bool              DistanceRamp;
        }

        private class LightTypeRenderState : IDisposable {
            public  readonly LightingRenderer           Parent;
            public  readonly LightTypeRenderStateKey    Key;
            public  readonly object                     Lock = new object();
            public  readonly UnorderedList<LightVertex> LightVertices = new UnorderedList<LightVertex>(512);
            public  readonly Material                   Material;

            private int                                 CurrentVertexCount = 0;
            private DynamicVertexBuffer                 LightVertexBuffer = null;

            public LightTypeRenderState (LightingRenderer parent, LightTypeRenderStateKey key) {
                Parent = parent;
                Key    = key;

                switch (key.Type) {
                    case LightSourceTypeID.Sphere:
                        Material = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.SphereLight
                            : (
                                key.DistanceRamp
                                    ? parent.IlluminantMaterials.SphereLightWithDistanceRamp
                                    : parent.IlluminantMaterials.SphereLightWithOpacityRamp
                            );
                        break;
                    case LightSourceTypeID.Directional:
                        Material = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.DirectionalLight
                            : parent.IlluminantMaterials.DirectionalLightWithRamp;
                        break;
                    default:
                        throw new NotImplementedException(key.Type.ToString());
                }
            }

            public void UpdateVertexBuffer () {
                lock (Lock) {
                    if ((LightVertexBuffer != null) && (LightVertexBuffer.VertexCount < LightVertices.Count)) {
                        Parent.Coordinator.DisposeResource(LightVertexBuffer);
                        LightVertexBuffer = null;
                    }

                    if (LightVertexBuffer == null) {
                        LightVertexBuffer = new DynamicVertexBuffer(
                            Parent.Coordinator.Device, typeof(LightVertex),
                            LightVertices.Capacity, BufferUsage.WriteOnly
                        );
                    }

                    LightVertexBuffer.SetData(LightVertices.GetBuffer(), 0, LightVertices.Count, SetDataOptions.Discard);
                    CurrentVertexCount = LightVertices.Count;
                }
            }

            public DynamicVertexBuffer GetVertexBuffer () {
                lock (Lock) {
                    if ((LightVertexBuffer == null) || CurrentVertexCount != LightVertices.Count)
                        throw new InvalidOperationException("Vertex buffer not up-to-date");

                    return LightVertexBuffer;
                }
            }

            public void Dispose () {
                lock (Lock) {
                    if (LightVertexBuffer != null) {
                        Parent.Coordinator.DisposeResource(LightVertexBuffer);
                        LightVertexBuffer = null;
                    }
                }
            }
        }

        private class LightObstructionTypeComparer : IComparer<LightObstruction> {
            public static readonly LightObstructionTypeComparer Instance = 
                new LightObstructionTypeComparer();

            public int Compare (LightObstruction lhs, LightObstruction rhs) {
                if (rhs == null) {
                    return (lhs == null) ? 0 : -1;
                } else if (lhs == null) {
                    return 0;
                }

                return ((int)lhs.Type) - ((int)rhs.Type);
            }
        }

        private struct TemplateUniforms {
            public Uniforms.Environment   Environment;
            public Uniforms.DistanceField DistanceField;
        }

        public const int MaximumLightCount = 4096;
        public const int PackedSliceCount = 3;
        public const int MaximumDistanceFunctionCount = 8192;

        const int        DistanceLimit = 520;
        
        public  readonly RenderCoordinator   Coordinator;
        public  readonly ContentManager      Content;

        public  readonly DefaultMaterialSet  Materials;
        public           IlluminantMaterials IlluminantMaterials { get; private set; }

        private readonly List<Material>      LoadedMaterials = new List<Material>();

        public  readonly DepthStencilState TopFaceDepthStencilState, FrontFaceDepthStencilState;
        public  readonly DepthStencilState DistanceInteriorStencilState, DistanceExteriorStencilState;
        public  readonly DepthStencilState SphereLightDepthStencilState;

        private readonly IndexBuffer         QuadIndexBuffer;

        private readonly DynamicVertexBuffer      DistanceFunctionVertexBuffer;
        private readonly DistanceFunctionVertex[] DistanceFunctionVertices = 
            new DistanceFunctionVertex[MaximumDistanceFunctionCount * 4];

        private readonly Dictionary<Polygon, Texture2D> HeightVolumeVertexData = 
            new Dictionary<Polygon, Texture2D>(new ReferenceComparer<Polygon>());

        private readonly RenderTarget2D _Lightmap;
        private readonly RenderTarget2D _PreviousLightmap;

        private DistanceField _DistanceField;
        private GBuffer _GBuffer;

        private byte[] _ReadbackBuffer;

        private readonly Action<DeviceManager, object> BeginLightPass, EndLightPass, IlluminationBatchSetup;

        private readonly object _LightStateLock = new object();
        private readonly Dictionary<LightTypeRenderStateKey, LightTypeRenderState> LightRenderStates = 
            new Dictionary<LightTypeRenderStateKey, LightTypeRenderState>();

        public readonly RendererConfiguration Configuration;
        public LightingEnvironment Environment;

        private static readonly short[] QuadIndices = new short[] {
            0, 1, 3, 1, 2, 3
        };

        private TemplateUniforms Uniforms = new TemplateUniforms();

        private string _Name;

        public LightingRenderer (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, LightingEnvironment environment,
            RendererConfiguration configuration
        ) {
            Materials = materials;
            Coordinator = coordinator;
            Configuration = configuration;
            Content = content;

            IlluminantMaterials = new IlluminantMaterials(materials);

            BeginLightPass     = _BeginLightPass;
            EndLightPass       = _EndLightPass;
            IlluminationBatchSetup = _IlluminationBatchSetup;

            lock (coordinator.CreateResourceLock) {
                QuadIndexBuffer = new IndexBuffer(
                    coordinator.Device, IndexElementSize.SixteenBits, MaximumLightCount * 6, BufferUsage.WriteOnly
                );
                DistanceFunctionVertexBuffer = new DynamicVertexBuffer(
                    coordinator.Device, typeof(DistanceFunctionVertex),
                    DistanceFunctionVertices.Length, BufferUsage.WriteOnly
                );

                FillIndexBuffer();

                _Lightmap = new RenderTarget2D(
                    coordinator.Device, 
                    Configuration.MaximumRenderSize.First, 
                    Configuration.MaximumRenderSize.Second,
                    false,
                    Configuration.HighQuality
                        ? SurfaceFormat.Rgba64
                        : SurfaceFormat.Color,
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                );

                if (Configuration.EnableBrightnessEstimation) {
                    var width = Configuration.MaximumRenderSize.First / 2;
                    var height = Configuration.MaximumRenderSize.Second / 2;

                    _PreviousLightmap = new RenderTarget2D(
                        coordinator.Device, 
                        width, height, true,
                        // TODO: Use SurfaceFormat.Single and do RGB->Gray conversion in shader
                        Configuration.HighQuality
                            ? SurfaceFormat.Rgba64
                            : SurfaceFormat.Color,
                        DepthFormat.None, 0, RenderTargetUsage.PreserveContents
                    );
                }
            }

            _GBuffer = new GBuffer(
                Coordinator, 
                Configuration.MaximumRenderSize.First, 
                Configuration.MaximumRenderSize.Second,
                Configuration.HighQuality
            );

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

            LoadMaterials(content);

            Environment = environment;

            Coordinator.DeviceReset += Coordinator_DeviceReset;
        }

        public string Name {
            get {
                return _Name;
            }
            set {
                _Name = value;
                this.SetName(value);
                NameSurfaces();
            }
        }

        private void NameSurfaces () {
            if (_Lightmap != null)
                _Lightmap.SetName(ObjectNames.ToObjectID(this) + ":Lightmap");
            if (_PreviousLightmap != null)
                _PreviousLightmap.SetName(ObjectNames.ToObjectID(this) + ":PreviousLightmap");
            if (_GBuffer != null)
                _GBuffer.Texture.SetName(ObjectNames.ToObjectID(this) + ":GBuffer");
        }

        public GBuffer GBuffer {
            get {
                return _GBuffer;
            }
        }

        public DistanceField DistanceField {
            get {
                return _DistanceField;
            }
            set {
                if (value == null)
                    throw new ArgumentNullException("DistanceField");
                _DistanceField = value;
            }
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            FillIndexBuffer();
        }

        private void FillIndexBuffer () {
            var buf = new short[QuadIndexBuffer.IndexCount];
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

            QuadIndexBuffer.SetData(buf);
        }

        public void Dispose () {
            foreach (var kvp in LightRenderStates)
                kvp.Value.Dispose();

            Coordinator.DisposeResource(QuadIndexBuffer);
            Coordinator.DisposeResource(DistanceField);
            Coordinator.DisposeResource(GBuffer);
            Coordinator.DisposeResource(_Lightmap);
            Coordinator.DisposeResource(_PreviousLightmap);

            foreach (var kvp in HeightVolumeVertexData)
                Coordinator.DisposeResource(kvp.Value);

            /*
            foreach (var m in LoadedMaterials)
                Coordinator.DisposeResource(m);
            */
        }

        private const int LuminanceScaleFactor = 8192;
        private const int RedScaleFactor = (int)(0.299 * LuminanceScaleFactor);
        private const int GreenScaleFactor = (int)(0.587 * LuminanceScaleFactor);
        private const int BlueScaleFactor = (int)(0.114 * LuminanceScaleFactor);

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
                    int luminance = ((pRgba[0] * RedScaleFactor) + (pRgba[1] * GreenScaleFactor) + (pRgba[2] * BlueScaleFactor)) / 65536;
                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                    sum += luminance;

                    if (luminance >= luminanceThreshold)
                        overThresholdCount += 1;
                }                
            } else {
                var pRgba = pBuffer;

                for (int i = 0; i < pixelCount; i++, pRgba += 4) {
                    int luminance = ((pRgba[0] * 257 * RedScaleFactor) + (pRgba[1] * 257 * GreenScaleFactor) + (pRgba[2] * 257 * BlueScaleFactor)) / 65536;
                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                    sum += luminance;

                    if (luminance >= luminanceThreshold)
                        overThresholdCount += 1;

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
        public void EstimateBrightness (
            Action<LightmapInfo> onComplete,
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

            if (_ReadbackBuffer == null)
                _ReadbackBuffer = new byte[count];

            Coordinator.AfterPresent(() => {
                _PreviousLightmap.GetData(
                    levelIndex, null,
                    _ReadbackBuffer, 0, count
                );

                var result = AnalyzeLightmap(_ReadbackBuffer, count, scaleFactor, threshold);
                onComplete(result);
            });
        }

        public RenderTarget2D Lightmap {
            get {
                return _Lightmap;
            }
        }

        private void ComputeUniforms () {
            if (DistanceField == null)
                throw new NullReferenceException("DistanceField");

            Uniforms = new TemplateUniforms {
                Environment = new Uniforms.Environment {
                    GroundZ = Environment.GroundZ,
                    ZToYMultiplier =
                        Configuration.TwoPointFiveD
                            ? Environment.ZToYMultiplier
                            : 0.0f,
                    InvZToYMultiplier =
                        Configuration.TwoPointFiveD
                            ? 1f / Environment.ZToYMultiplier
                            : 0.0f,
                    RenderScale = Configuration.RenderScale
                },

                DistanceField = new Uniforms.DistanceField {
                    Extent = new Vector3(
                        DistanceField.VirtualWidth,
                        DistanceField.VirtualHeight,
                        Environment.MaximumZ
                    ),
                    TextureSliceSize = new Vector2(1f / DistanceField.ColumnCount, 1f / DistanceField.RowCount),
                    TextureSliceCount = new Vector3(DistanceField.ColumnCount, DistanceField.RowCount, DistanceField.ValidSliceCount),
                    TextureTexelSize = new Vector2(
                        1f / (DistanceField.VirtualWidth * DistanceField.ColumnCount), 
                        1f / (DistanceField.VirtualHeight * DistanceField.RowCount)
                    ),
                    InvScaleFactor = 1f / DistanceField.Resolution,
                    OcclusionToOpacityPower = Configuration.DistanceFieldOcclusionToOpacityPower,
                    MaxConeRadius = Configuration.DistanceFieldMaxConeRadius,
                    ConeGrowthFactor = Configuration.DistanceFieldConeGrowthFactor,
                    Step = new Vector3(
                        (float)Configuration.DistanceFieldMaxStepCount,
                        Configuration.DistanceFieldMinStepSize,
                        Configuration.DistanceFieldLongStepFactor
                    )
                }
            };
        }

        private void _BeginLightPass (DeviceManager device, object userData) {
            device.Device.Viewport = new Viewport(0, 0, Configuration.RenderSize.First, Configuration.RenderSize.Second);

            var vt = ViewTransform.CreateOrthographic(
                Configuration.RenderSize.First, Configuration.RenderSize.Second
            );
            vt.Position = Materials.ViewportPosition;
            vt.Scale = Materials.ViewportScale;

            device.PushStates();
            Materials.PushViewTransform(ref vt);
        }

        private void _EndLightPass (DeviceManager device, object userData) {
            Materials.PopViewTransform();
            device.PopStates();
        }

        private void _IlluminationBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;
            lock (_LightStateLock)
                ltrs.UpdateVertexBuffer();

            device.Device.BlendState = RenderStates.AdditiveBlend;

            SetLightShaderParameters(ltrs.Material);
            ltrs.Material.Effect.Parameters["RampTexture"].SetValue(ltrs.Key.RampTexture);
        }

        private void SetLightShaderParameters (Material material) {
            var effect = material.Effect;
            var p = effect.Parameters;

            // FIXME: RenderScale?
            p["GBufferTexelSize"].SetValue(GBuffer.InverseSize);
            p["GBuffer"].SetValue(GBuffer.Texture);

            var ub = Materials.GetUniformBinding<Uniforms.Environment>(material, "Environment");
            ub.Value.Current = Uniforms.Environment;

            SetDistanceFieldParameters(material, true);
        }

        private LightTypeRenderState GetLightRenderState (LightSource ls) {
            var ltk =
                new LightTypeRenderStateKey {
                    Type = ls.TypeID,
                    RampTexture = ls.RampTexture ?? Configuration.DefaultRampTexture
                };

            // A 1x1 ramp is treated as no ramp at all.
            // This lets you override a default ramp texture on a per-light basis by attaching a 1x1 ramp,
            //  and means if you have a ramp turned on by default you can shut it off by replacing the
            //  image file with a 1x1 bitmap
            if (
                (ltk.RampTexture != null) &&
                (ltk.RampTexture.Width == 1)
            )
                ltk.RampTexture = null;

            var sls = ls as SphereLightSource;
            if (sls != null)
                ltk.DistanceRamp = sls.UseDistanceForRampTexture;

            LightTypeRenderState result;
            if (!LightRenderStates.TryGetValue(ltk, out result)) {
                LightRenderStates[ltk] = result = new LightTypeRenderState(
                    this, ltk
                );
            }

            return result;
        }

        /// <summary>
        /// Updates the lightmap in the target batch container on the specified layer.
        /// To display lighting, use ResolveLighting.
        /// </summary>
        /// <param name="container">The batch container to render lighting into.</param>
        /// <param name="layer">The layer to render lighting into.</param>
        /// <param name="intensityScale">A factor to scale the intensity of all light sources. You can use this to rescale the intensity of light values for HDR.</param>
        public void RenderLighting (
            IBatchContainer container, int layer, float intensityScale = 1.0f
        ) {
            int layerIndex = 0;

            ComputeUniforms();

            using (var outerGroup = BatchGroup.New(container, layer)) {
                // HACK: We make a copy of the previous lightmap so that brightness estimation can read it, without
                //  stalling on the current lightmap being rendered
                if (Configuration.EnableBrightnessEstimation)
                using (var copyGroup = BatchGroup.ForRenderTarget(
                    outerGroup, 0, _PreviousLightmap,
                    (dm, _) => {
                        Materials.PushViewTransform(ViewTransform.CreateOrthographic(_PreviousLightmap.Width, _PreviousLightmap.Height));
                    },
                    (dm, _) => {
                        Materials.PopViewTransform();
                    }
                )) {
                    if (RenderTrace.EnableTracing)
                        RenderTrace.Marker(copyGroup, -1, "LightingRenderer {0} : Generate HDR Buffer", this.ToObjectID());

                    var ir = new ImperativeRenderer(
                        copyGroup, Materials, 
                        blendState: BlendState.Opaque,
                        samplerState: SamplerState.LinearClamp,
                        worldSpace: false
                    );
                    ir.Clear(color: Color.Transparent);
                    ir.Draw(_Lightmap, new Rectangle(0, 0, _PreviousLightmap.Width, _PreviousLightmap.Height));
                }

                using (var resultGroup = BatchGroup.ForRenderTarget(outerGroup, 1, _Lightmap, before: BeginLightPass, after: EndLightPass)) {
                    if (RenderTrace.EnableTracing)
                        RenderTrace.Marker(resultGroup, -9999, "LightingRenderer {0} : Begin", this.ToObjectID());

                    ClearBatch.AddNew(
                        resultGroup, -1, Materials.Clear, new Color(0, 0, 0, Configuration.RenderGroundPlane ? 1f : 0f)
                    );

                    int j = 0;

                    // TODO: Use threads?
                    lock (_LightStateLock) {
                        foreach (var kvp in LightRenderStates)
                            kvp.Value.LightVertices.Clear();

                        using (var buffer = BufferPool<LightSource>.Allocate(Environment.Lights.Count)) {
                            Environment.Lights.CopyTo(buffer.Data);
                            Squared.Util.Sort.FastCLRSort(
                                buffer.Data, LightSorter.Instance, 0, Environment.Lights.Count
                            );

                            for (var i = 0; i < Environment.Lights.Count; i++) {
                                var lightSource = buffer.Data[i];
                                var pointLightSource = lightSource as SphereLightSource;
                                var directionalLightSource = lightSource as DirectionalLightSource;

                                var ltrs = GetLightRenderState(lightSource);

                                if (pointLightSource != null)
                                    RenderPointLightSource(pointLightSource, intensityScale, ltrs);
                                else if (directionalLightSource != null)
                                    RenderDirectionalLightSource(directionalLightSource, intensityScale, ltrs);
                                else
                                    throw new NotSupportedException(lightSource.GetType().Name);
                            };
                        }

                        foreach (var kvp in LightRenderStates)
                            kvp.Value.UpdateVertexBuffer();

                        foreach (var kvp in LightRenderStates) {
                            var ltrs = kvp.Value;
                            var count = ltrs.LightVertices.Count / 4;
                            if (count <= 0)
                                continue;

                            if (RenderTrace.EnableTracing)
                                RenderTrace.Marker(resultGroup, layerIndex++, "LightingRenderer {0} : Render {1} {2} light(s)", this.ToObjectID(), count, ltrs.Key.Type);

                            using (var nb = NativeBatch.New(
                                resultGroup, layerIndex++, ltrs.Material, IlluminationBatchSetup, userData: ltrs
                            )) {
                                nb.Add(new NativeDrawCall(
                                    PrimitiveType.TriangleList,
                                    ltrs.GetVertexBuffer(), 0,
                                    QuadIndexBuffer, 0, 0, ltrs.LightVertices.Count, 0, ltrs.LightVertices.Count / 2
                                ));
                            }
                        }
                    }

                    if (RenderTrace.EnableTracing)
                        RenderTrace.Marker(resultGroup, 9999, "LightingRenderer {0} : End", this.ToObjectID());
                }
            }
        }

        private void RenderPointLightSource (SphereLightSource lightSource, float intensityScale, LightTypeRenderState ltrs) {
            LightVertex vertex;
            vertex.LightCenter = lightSource.Position;
            vertex.Color = lightSource.Color;
            vertex.Color.W *= (lightSource.Opacity * intensityScale);
            vertex.LightProperties.X = lightSource.Radius;
            vertex.LightProperties.Y = lightSource.RampLength;
            vertex.LightProperties.Z = (int)lightSource.RampMode;
            vertex.LightProperties.W = lightSource.CastsShadows ? 1f : 0f;
            vertex.MoreLightProperties.X = lightSource.AmbientOcclusionRadius;
            vertex.MoreLightProperties.Y = lightSource.ShadowDistanceFalloff.GetValueOrDefault(-99999);
            vertex.MoreLightProperties.Z = lightSource.FalloffYFactor;

            vertex.Position = new Vector2(0, 0);
            ltrs.LightVertices.Add(ref vertex);
            vertex.Position = new Vector2(1, 0);
            ltrs.LightVertices.Add(ref vertex);
            vertex.Position = new Vector2(1, 1);
            ltrs.LightVertices.Add(ref vertex);
            vertex.Position = new Vector2(0, 1);
            ltrs.LightVertices.Add(ref vertex);
        }

        private void RenderDirectionalLightSource (DirectionalLightSource lightSource, float intensityScale, LightTypeRenderState ltrs) {
            var lightDirection = lightSource.Direction;
            lightDirection.Normalize();

            LightVertex vertex;
            vertex.LightCenter = lightDirection;
            vertex.Color = lightSource.Color;
            vertex.Color.W *= (lightSource.Opacity * intensityScale);
            vertex.LightProperties = new Vector4(
                lightSource.CastsShadows ? 1f : 0f,
                lightSource.ShadowTraceLength,
                lightSource.ShadowSoftness,
                lightSource.ShadowRampRate
            );
            vertex.MoreLightProperties = new Vector3(
                lightSource.AmbientOcclusionRadius,
                lightSource.ShadowDistanceFalloff.GetValueOrDefault(-99999),
                lightSource.ShadowRampLength
            );

            var lightBounds = new Bounds(
                Vector2.Zero,
                new Vector2(
                    Configuration.MaximumRenderSize.First,
                    Configuration.MaximumRenderSize.Second
                )
            );

            vertex.Position = lightBounds.TopLeft;
            ltrs.LightVertices.Add(ref vertex);
            vertex.Position = lightBounds.TopRight;
            ltrs.LightVertices.Add(ref vertex);
            vertex.Position = lightBounds.BottomRight;
            ltrs.LightVertices.Add(ref vertex);
            vertex.Position = lightBounds.BottomLeft;
            ltrs.LightVertices.Add(ref vertex);
        }

        /// <summary>
        /// Resolves the current lightmap into the specified batch container on the specified layer.
        /// </summary>
        /// <param name="container">The batch container to resolve lighting into.</param>
        /// <param name="layer">The layer to resolve lighting into.</param>
        public void ResolveLighting (
            IBatchContainer container, int layer, 
            float? width = null, float? height = null, 
            HDRConfiguration? hdr = null
        ) {
            Material m;
            if (hdr.HasValue && hdr.Value.Mode == HDRMode.GammaCompress)
                m = IlluminantMaterials.GammaCompressedLightingResolve;
            else if (hdr.HasValue && hdr.Value.Mode == HDRMode.ToneMap)
                m = IlluminantMaterials.ToneMappedLightingResolve;
            else
                m = IlluminantMaterials.LightingResolve;

            // HACK: This is a little gross
            var ir = new ImperativeRenderer(container, Materials, layer);
            var sg = ir.MakeSubgroup(before: (dm, _) => {
                // FIXME: RenderScale?
                m.Effect.Parameters["GBufferTexelSize"].SetValue(GBuffer.InverseSize);
                m.Effect.Parameters["GBuffer"].SetValue(GBuffer.Texture);
                m.Effect.Parameters["InverseScaleFactor"].SetValue(
                    hdr.HasValue
                        ? hdr.Value.InverseScaleFactor
                        : 1.0f
                );

                var ub = Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                ub.Value.Current = Uniforms.Environment;

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

            var bounds = _Lightmap.BoundsFromRectangle(
                new Rectangle(0, 0, Configuration.RenderSize.First, Configuration.RenderSize.Second)
            );

            var dc = new BitmapDrawCall(
                _Lightmap, Vector2.Zero, bounds                
            );
            dc.Scale = new Vector2(
                width.GetValueOrDefault(Configuration.RenderSize.First) / Configuration.RenderSize.First,
                height.GetValueOrDefault(Configuration.RenderSize.Second) / Configuration.RenderSize.Second
            );

            sg.Draw(dc, material: m);
        }

        private Vector4 AddW (Vector3 v3) {
            return new Vector4(v3, 1);
        }

        private Vector3 StripW (Vector4 v4) {
            return new Vector3(v4.X / v4.W, v4.Y / v4.W, v4.Z / v4.W);
        }

        // XNA is garbage and BoundingBox intersections don't work
        private Vector3? FindBoxIntersection (Ray ray, Vector3 boxMin, Vector3 boxMax) {
            Plane    plane;
            float    minDistance = 999999;
            Vector3? result = null;

            for (int i = 0; i < 6; i++) {
                switch (i) {
                    case 0:
                        plane = new Plane(Vector3.UnitX, boxMin.X);
                        break;
                    case 1:
                        plane = new Plane(Vector3.UnitY, boxMin.X);
                        break;
                    case 2:
                        plane = new Plane(Vector3.UnitZ, boxMin.X);
                        break;
                    case 3:
                        plane = new Plane(-Vector3.UnitX, boxMax.X);
                        break;
                    case 4:
                        plane = new Plane(-Vector3.UnitY, boxMax.Y);
                        break;
                    case 5:
                        plane = new Plane(-Vector3.UnitZ, boxMax.Z);
                        break;
                    default:
                        throw new Exception();
                }

                float? intersection = ray.Intersects(plane);
                if (!intersection.HasValue)
                    continue;

                if (intersection.Value > minDistance)
                    continue;

                minDistance = intersection.Value;
                result = ray.Position + (ray.Direction * intersection.Value);
            }

            return result;
        }

        public VisualizationInfo VisualizeDistanceField (
            Bounds rectangle, 
            Vector3 viewDirection,
            IBatchContainer container, int layerIndex,
            LightObstruction singleObject = null,
            VisualizationMode mode = VisualizationMode.Surfaces,
            BlendState blendState = null,
            Vector4? color = null,
            Vector3? ambientColor = null,
            Vector3? lightColor = null,
            Vector3? lightDirection = null
        ) {
            ComputeUniforms();

            var tl = new Vector3(rectangle.TopLeft, 0);
            var tr = new Vector3(rectangle.TopRight, 0);
            var bl = new Vector3(rectangle.BottomLeft, 0);
            var br = new Vector3(rectangle.BottomRight, 0);

            var extent = new Vector3(
                DistanceField.VirtualWidth,
                DistanceField.VirtualHeight,
                Environment.MaximumZ
            );
            var center = extent * 0.5f;
            var halfTexel = new Vector3(-0.5f * (1.0f / extent.X), -0.5f * (1.0f / extent.Y), 0);

            // HACK: Pick an appropriate length that will always travel through the whole field
            var rayLength = extent.Length() * 1.5f;
            var rayVector = viewDirection * rayLength;
            Vector3 rayOrigin;

            // HACK: Place our view plane somewhere reasonable
            {
                var ray = new Ray(center, -viewDirection);
                var planeCenter = FindBoxIntersection(ray, Vector3.Zero, extent);
                if (!planeCenter.HasValue)
                    throw new Exception("Ray didn't intersect the box... what?");
                rayOrigin = planeCenter.Value;
            }

            Vector3 worldTL, worldTR, worldBL, worldBR;

            Vector3 right, up;

            // HACK: Fuck matrices, they never work
            if (viewDirection.Z != 0) {
                up = -Vector3.UnitY;
            } else {
                up = Vector3.UnitZ;
            }

            if (viewDirection.X != 0) {
                right = Vector3.UnitY;
            } else {
                right = Vector3.UnitX;
            }

            var absViewDirection = new Vector3(
                Math.Abs(viewDirection.X),
                Math.Abs(viewDirection.Y),
                Math.Abs(viewDirection.Z)
            );
            var planeRight = Vector3.Cross(absViewDirection, up);
            var planeUp = Vector3.Cross(absViewDirection, right);

            worldTL = rayOrigin + (-planeRight * center) + (-planeUp  * center);
            worldTR = rayOrigin + (planeRight  * center) + (-planeUp  * center);
            worldBL = rayOrigin + (-planeRight * center) + (planeUp * center);
            worldBR = rayOrigin + (planeRight  * center) + (planeUp * center);

            var _color = color.GetValueOrDefault(Vector4.One);

            var verts = new VisualizeDistanceFieldVertex[] {
                new VisualizeDistanceFieldVertex {
                    Position = tl + halfTexel,
                    RayStart = worldTL,
                    RayVector = rayVector,
                    Color = _color
                },
                new VisualizeDistanceFieldVertex {
                    Position = tr + halfTexel,
                    RayStart = worldTR,
                    RayVector = rayVector,
                    Color = _color
                },
                new VisualizeDistanceFieldVertex {
                    Position = br + halfTexel,
                    RayStart = worldBR,
                    RayVector = rayVector,
                    Color = _color
                },
                new VisualizeDistanceFieldVertex {
                    Position = bl + halfTexel,
                    RayStart = worldBL,
                    RayVector = rayVector,
                    Color = _color
                }
            };

            Render.Material material = null;

            if (singleObject != null) {
                material = mode == VisualizationMode.Outlines
                    ? IlluminantMaterials.FunctionOutline
                    : IlluminantMaterials.FunctionSurface;
            } else {
                material = mode == VisualizationMode.Outlines
                    ? IlluminantMaterials.ObjectOutlines
                    : IlluminantMaterials.ObjectSurfaces;
            }

            material = Materials.Get(
                material,
                depthStencilState: DepthStencilState.None,
                rasterizerState: RasterizerState.CullNone,
                blendState: blendState ?? BlendState.AlphaBlend
            );
            using (var batch = PrimitiveBatch<VisualizeDistanceFieldVertex>.New(
                container, layerIndex++, material, (dm, _) => {
                    var p = material.Effect.Parameters;

                    SetDistanceFieldParameters(material, true);

                    var ac = ambientColor.GetValueOrDefault(new Vector3(0.1f, 0.15f, 0.15f));
                    p["AmbientColor"].SetValue(ac);

                    var ld = lightDirection.GetValueOrDefault(new Vector3(0, -0.5f, -1.0f));
                    ld.Normalize();
                    p["LightDirection"].SetValue(ld);

                    var lc = lightColor.GetValueOrDefault(new Vector3(0.75f));
                    p["LightColor"].SetValue(lc);

                    if (singleObject != null) {
                        p["FunctionType"].SetValue((int)singleObject.Type);
                        p["FunctionCenter"].SetValue(singleObject.Center);
                        p["FunctionSize"].SetValue(singleObject.Size);
                    }
                }
            )) {
                batch.Add(new PrimitiveDrawCall<VisualizeDistanceFieldVertex>(
                    PrimitiveType.TriangleList, verts, 0, 4, QuadIndices, 0, 2
                ));
            }

            return new VisualizationInfo {
                ViewCenter = rayOrigin,
                Up = planeUp,
                Right = planeRight
            };
        }

        private void SetDistanceFieldParameters (Material m, bool setDistanceTexture) {
            var p = m.Effect.Parameters;

            p["MaximumEncodedDistance"].SetValue(DistanceField.MaximumEncodedDistance);

            Materials.TrySetBoundUniform(m, "DistanceField", ref Uniforms.DistanceField);
            Materials.TrySetBoundUniform(m, "Environment", ref Uniforms.Environment);

            if (setDistanceTexture)
                p["DistanceFieldTexture"].SetValue(DistanceField.Texture);
        }

        public void InvalidateFields (
            // TODO: Maybe remove this since I'm not sure it's useful at all.
            Bounds3? region = null
        ) {
            if (GBuffer != null)
                GBuffer.Invalidate();
            if (DistanceField != null)
                DistanceField.Invalidate();
        }

        public void UpdateFields (IBatchContainer container, int layer) {
            ComputeUniforms();

            if (!GBuffer.IsValid) {
                RenderGBuffer(ref layer, container);

                if (Configuration.GBufferCaching)
                    GBuffer.IsValid = true;
            }

            if (DistanceField.InvalidSlices.Count > 0) {
                RenderDistanceField(ref layer, container);
            }
        }

        private void RenderGBuffer (
            ref int layerIndex, IBatchContainer resultGroup,
            bool enableHeightVolumes = true, bool enableBillboards = true
        ) {
            // FIXME: Is this right?
            var renderWidth = (int)(Configuration.MaximumRenderSize.First / Configuration.RenderScale.X);
            var renderHeight = (int)(Configuration.MaximumRenderSize.Second / Configuration.RenderScale.Y);

            using (var group = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex, GBuffer.Texture,
                // FIXME: Optimize this
                (dm, _) => {
                    Materials.PushViewTransform(ViewTransform.CreateOrthographic(
                        renderWidth, renderHeight
                    ));
                },
                (dm, _) => {
                    Materials.PopViewTransform();
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
                        p["DistanceFieldExtent"].SetValue(Uniforms.DistanceField.Extent);

                        var ub = Materials.GetUniformBinding<Uniforms.Environment>(IlluminantMaterials.HeightVolumeFace, "Environment");
                        ub.Value.Current = Uniforms.Environment;

                        ub = Materials.GetUniformBinding<Uniforms.Environment>(IlluminantMaterials.HeightVolume, "Environment");
                        ub.Value.Current = Uniforms.Environment;
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

        private void RenderGBufferBillboards (IBatchContainer container, int layerIndex) {
            int i = 0;

            // FIXME: GC pressure
            var verts = new BillboardVertex[4 * Environment.Billboards.Count];

            Action<DeviceManager, object> setTexture = (dm, _) => {
                var billboard = (Billboard)_;
                var material =
                    billboard.Type == BillboardType.Mask
                        ? IlluminantMaterials.MaskBillboard
                        : IlluminantMaterials.GDataBillboard;
                material.Effect.Parameters["Mask"].SetValue(billboard.Texture);
                material.Flush();
            };

            using (var maskBatch = PrimitiveBatch<BillboardVertex>.New(
                container, layerIndex++, Materials.Get(
                    IlluminantMaterials.MaskBillboard,
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RasterizerState.CullNone,
                    blendState: BlendState.Opaque
                ), (dm, _) => {
                    var material = IlluminantMaterials.MaskBillboard;
                    Materials.TrySetBoundUniform(material, "Environment", ref Uniforms.Environment);
                    material.Effect.Parameters["DistanceFieldExtent"].SetValue(Uniforms.DistanceField.Extent);
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
                    Materials.TrySetBoundUniform(material, "Environment", ref Uniforms.Environment);
                    material.Effect.Parameters["DistanceFieldExtent"].SetValue(Uniforms.DistanceField.Extent);
                }
            )) 
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

                var j = i * 4;
                verts[j + 0] = new BillboardVertex {
                    Position = tl,
                    Normal = normal1,
                    WorldPosition = bl + new Vector3(0, 0, size.Z),
                    TexCoord = Vector2.Zero,
                    DataScale = billboard.DataScale,
                };
                verts[j + 1] = new BillboardVertex {
                    Position = tr,
                    Normal = normal2,
                    WorldPosition = bl + new Vector3(size.X, 0, size.Z),
                    TexCoord = new Vector2(1, 0),
                    DataScale = billboard.DataScale,
                };
                verts[j + 2] = new BillboardVertex {
                    Position = tl + size,
                    Normal = normal2,
                    WorldPosition = bl + new Vector3(size.X, 0, 0),
                    TexCoord = Vector2.One,
                    DataScale = billboard.DataScale,
                };
                verts[j + 3] = new BillboardVertex {
                    Position = bl,
                    Normal = normal1,
                    WorldPosition = bl,
                    TexCoord = new Vector2(0, 1),
                    DataScale = billboard.DataScale,
                };

                var batch =
                    billboard.Type == BillboardType.GBufferData
                        ? gDataBatch
                        : maskBatch;

                batch.Add(new PrimitiveDrawCall<BillboardVertex>(
                    PrimitiveType.TriangleList, verts, j, 4, 
                    QuadIndices, 0, 2, 
                    new DrawCallSortKey(order: i),
                    setTexture, billboard
                ));

                i++;
            }
        }

        private void RenderDistanceField (ref int layerIndex, IBatchContainer resultGroup) {           
            int sliceCount = DistanceField.SliceCount;
            int slicesToUpdate =
                Math.Min(
                    Configuration.DistanceFieldUpdateRate,
                    DistanceField.InvalidSlices.Count
                );
            if (slicesToUpdate <= 0)
                return;

            using (var rtGroup = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex++, DistanceField.Texture,
                // HACK: Since we're mucking with view transforms, do a save and restore
                (dm, _) => {
                    Materials.PushViewTransform(Materials.ViewTransform);
                },
                (dm, _) => {
                    Materials.PopViewTransform();
                }
            )) {
                // We incrementally do a partial update of the distance field.
                int layer = 0;
                while (slicesToUpdate > 0) {
                    var slice = DistanceField.InvalidSlices[0];
                    var physicalSlice = slice / PackedSliceCount;

                    RenderDistanceFieldSliceTriplet(
                        rtGroup, physicalSlice, slice, ref layer
                    );

                    slicesToUpdate -= 3;
                }
            }
        }

        private float SliceIndexToZ (int slice) {
            float sliceZ = (slice / Math.Max(1, (float)(DistanceField.SliceCount - 1)));
            return sliceZ * Environment.MaximumZ;
        }

        private void RenderDistanceFieldSliceTriplet (
            BatchGroup rtGroup, int physicalSliceIndex, int firstVirtualSliceIndex, ref int layer
        ) {
            var interior = IlluminantMaterials.DistanceFieldInterior;
            var exterior = IlluminantMaterials.DistanceFieldExterior;

            var sliceX = (physicalSliceIndex % DistanceField.ColumnCount) * DistanceField.SliceWidth;
            var sliceY = (physicalSliceIndex / DistanceField.ColumnCount) * DistanceField.SliceHeight;
            var sliceXVirtual = (physicalSliceIndex % DistanceField.ColumnCount) * DistanceField.VirtualWidth;
            var sliceYVirtual = (physicalSliceIndex / DistanceField.ColumnCount) * DistanceField.VirtualHeight;

            var viewTransform = ViewTransform.CreateOrthographic(
                0, 0, 
                (int)Math.Ceiling(DistanceField.VirtualWidth * DistanceField.ColumnCount), 
                (int)Math.Ceiling(DistanceField.VirtualHeight * DistanceField.RowCount)
            );
            viewTransform.Position = new Vector2(-sliceXVirtual, -sliceYVirtual);

            Action<DeviceManager, object> beginSliceBatch =
                (dm, _) => {
                    // TODO: Optimize this
                    dm.Device.ScissorRectangle = new Rectangle(
                        sliceX, sliceY, DistanceField.SliceWidth, DistanceField.SliceHeight
                    );

                    Materials.ApplyViewTransformToMaterial(IlluminantMaterials.ClearDistanceFieldSlice, ref viewTransform);
                    Materials.ApplyViewTransformToMaterial(interior, ref viewTransform);
                    Materials.ApplyViewTransformToMaterial(exterior, ref viewTransform);

                    SetDistanceFieldParameters(interior, false);
                    SetDistanceFieldParameters(exterior, false);

                    foreach (var m in IlluminantMaterials.DistanceFunctionTypes) {
                        Materials.ApplyViewTransformToMaterial(m, ref viewTransform);
                        SetDistanceFieldParameters(m, false);
                    }
                };

            var lastVirtualSliceIndex = firstVirtualSliceIndex + 2;

            using (var group = BatchGroup.New(rtGroup, layer++,
                beginSliceBatch, null
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, -2, "LightingRenderer {0} : Begin Distance Field Slices [{1}-{2}]", this.ToObjectID(), firstVirtualSliceIndex, lastVirtualSliceIndex);

                ClearDistanceFieldSlice(
                    QuadIndices, group, -1, firstVirtualSliceIndex
                );

                RenderDistanceFieldDistanceFunctions(firstVirtualSliceIndex, group);
                RenderDistanceFieldHeightVolumes(firstVirtualSliceIndex, group);

                // FIXME: Slow
                for (var i = firstVirtualSliceIndex; i <= lastVirtualSliceIndex; i++)
                    DistanceField.InvalidSlices.Remove(i);

                DistanceField.ValidSliceCount = Math.Max(DistanceField.ValidSliceCount, lastVirtualSliceIndex + 1);

                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, 9999, "LightingRenderer {0} : End Distance Field Slices [{1}-{2}]", this.ToObjectID(), firstVirtualSliceIndex, lastVirtualSliceIndex);
            }
        }

        private void RenderDistanceFieldHeightVolumes (
            int firstVirtualIndex, BatchGroup group
        ) {
            int i = 1;

            var interior = IlluminantMaterials.DistanceFieldInterior;
            var exterior = IlluminantMaterials.DistanceFieldExterior;
            var sliceZ = new Vector4(
                SliceIndexToZ(firstVirtualIndex),
                SliceIndexToZ(firstVirtualIndex + 1),
                SliceIndexToZ(firstVirtualIndex + 2),
                SliceIndexToZ(firstVirtualIndex + 3)
            );

            // Rasterize the height volumes in sequential order.
            // FIXME: Depth buffer/stencil buffer tricks should work for generating this SDF, but don't?
            using (var interiorGroup = BatchGroup.New(group, 1, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(interior, false);
            }))
            using (var exteriorGroup = BatchGroup.New(group, 2, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(exterior, false);
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

                // FIXME: Hoist these out and use BeforeDraw
                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    interiorGroup, i, IlluminantMaterials.DistanceFieldInterior,
                    (dm, _) => {
                        var ep = interior.Effect.Parameters;
                        ep["NumVertices"].SetValue(p.Count);
                        ep["VertexDataTexture"].SetValue(vertexDataTexture);
                        ep["SliceZ"].SetValue(sliceZ);
                    }
                ))
                    batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                        PrimitiveType.TriangleList,
                        m, 0, m.Length / 3
                    ));

                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    exteriorGroup, i, IlluminantMaterials.DistanceFieldExterior,
                    (dm, _) => {
                        var ep = exterior.Effect.Parameters;
                        ep["NumVertices"].SetValue(p.Count);
                        ep["VertexDataTexture"].SetValue(vertexDataTexture);
                        ep["SliceZ"].SetValue(sliceZ);
                    }
                ))
                    batch.Add(new PrimitiveDrawCall<HeightVolumeVertex>(
                        PrimitiveType.TriangleList,
                        boundingBoxVertices, 0, boundingBoxVertices.Length, QuadIndices, 0, 2
                    ));

                i++;
            }
        }

        private void ClearDistanceFieldSlice (
            short[] indices, IBatchContainer container, int layer, int firstSliceIndex
        ) {
            // var color = new Color((firstSliceIndex * 16) % 255, 0, 0, 0);
            var color = Color.Transparent;

            var verts = new VertexPositionColor[] {
                new VertexPositionColor(new Vector3(0, 0, 0), color),
                new VertexPositionColor(new Vector3(DistanceField.VirtualWidth, 0, 0), color),
                new VertexPositionColor(new Vector3(DistanceField.VirtualWidth, DistanceField.VirtualHeight, 0), color),
                new VertexPositionColor(new Vector3(0, DistanceField.VirtualHeight, 0), color)
            };

            using (var batch = PrimitiveBatch<VertexPositionColor>.New(
                container, layer, IlluminantMaterials.ClearDistanceFieldSlice
            ))
                batch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                    PrimitiveType.TriangleList,
                    verts, 0, 4, indices, 0, 2
                ));
        }

        // HACK
        bool DidUploadDistanceFieldBuffer = false;

        private void RenderDistanceFieldDistanceFunctions (
            int firstVirtualIndex, BatchGroup group
        ) {
            var items = Environment.Obstructions;
            if (items.Count <= 0)
                return;

            var sliceZ = new Vector4(
                SliceIndexToZ(firstVirtualIndex),
                SliceIndexToZ(firstVirtualIndex + 1),
                SliceIndexToZ(firstVirtualIndex + 2),
                SliceIndexToZ(firstVirtualIndex + 3)
            );

            // todo: shrink these per-instance?
            var tl = new Vector3(0, 0, 0);
            var tr = new Vector3(DistanceField.VirtualWidth, 0, 0);
            var br = new Vector3(DistanceField.VirtualWidth, DistanceField.VirtualHeight, 0);
            var bl = new Vector3(0, DistanceField.VirtualHeight, 0);

            var numTypes    = (int)LightObstructionType.MAX + 1;
            var batches     = new NativeBatch[numTypes];
            var firstOffset = new int[numTypes];
            var primCount   = new int[numTypes];

            try {
                for (int i = 0; i < numTypes; i++) {
                    var m = IlluminantMaterials.DistanceFunctionTypes[i];

                    Action<DeviceManager, object> setup = null;

                    setup = (dm, _) => {
                        m.Effect.Parameters["SliceZ"].SetValue(sliceZ);

                        lock (DistanceFunctionVertices) {
                            if (DidUploadDistanceFieldBuffer)
                                return;

                            if (items.Count > 0)
                                DistanceFunctionVertexBuffer.SetData(DistanceFunctionVertices, 0, items.Count * 4, SetDataOptions.Discard);

                            DidUploadDistanceFieldBuffer = true;
                        }
                    };

                    if (RenderTrace.EnableTracing)
                        RenderTrace.Marker(group, (i * 2) + 3, "LightingRenderer {0} : Render {1}(s)", this.ToObjectID(), (LightObstructionType)i);
                    
                    batches[i] = NativeBatch.New(
                        group, (i * 2) + 4, m, setup
                    );
                    firstOffset[i] = -1;
                }

                // HACK: Sort all the functions by type, fill the VB with each group,
                //  then issue a single draw for each
                using (var buffer = BufferPool<LightObstruction>.Allocate(items.Count))
                lock (DistanceFunctionVertices) {
                    Array.Clear(buffer.Data, 0, buffer.Data.Length);
                    items.CopyTo(buffer.Data);
                    Array.Sort(buffer.Data, 0, items.Count, LightObstructionTypeComparer.Instance);

                    DidUploadDistanceFieldBuffer = false;

                    int j = 0;
                    for (int i = 0; i < items.Count; i++) {
                        var item = buffer.Data[i];
                        var type = (int)item.Type;

                        if (firstOffset[type] == -1)
                            firstOffset[type] = j;

                        primCount[type] += 2;

                        // See definition of DISTANCE_MAX in DistanceFieldCommon.fxh
                        float offset = DistanceField.MaximumEncodedDistance + 1;

                        tl = new Vector3(item.Center.X - item.Size.X - offset, item.Center.Y - item.Size.Y - offset, 0);
                        br = new Vector3(item.Center.X + item.Size.X + offset, item.Center.Y + item.Size.Y + offset, 0);
                        tr = new Vector3(br.X, tl.Y, 0);
                        bl = new Vector3(tl.X, br.Y, 0);

                        DistanceFunctionVertices[j++] = new DistanceFunctionVertex(
                            tl, item.Center, item.Size
                        );
                        DistanceFunctionVertices[j++] = new DistanceFunctionVertex(
                            tr, item.Center, item.Size
                        );
                        DistanceFunctionVertices[j++] = new DistanceFunctionVertex(
                            br, item.Center, item.Size
                        );
                        DistanceFunctionVertices[j++] = new DistanceFunctionVertex(
                            bl, item.Center, item.Size
                        );
                    }

                    for (int i = 0; i < numTypes; i++) {
                        if (primCount[i] <= 0)
                            continue;

                        batches[i].Add(new NativeDrawCall(
                            PrimitiveType.TriangleList,
                            DistanceFunctionVertexBuffer, 0, 
                            QuadIndexBuffer, firstOffset[i], 0, primCount[i] * 2,
                            0, primCount[i]
                        ));
                    }
                }
            } finally {
                foreach (var batch in batches)
                    batch.Dispose();
            }
        }
        
        /// <summary>
        /// When using a lightmap resolution below 1.0, if you set the view position in raw pixels
        /// you will get weird jittery artifacts when scrolling as the alignment of obstructions
        /// and lights changes relative to the lightmap pixels. Use this method to compensate by
        /// using a combination of a view position and a UV offset.
        /// </summary>
        /// <param name="viewPosition">The view position in raw pixels.</param>
        /// <param name="computedViewPosition">The view position you should actually set in your ViewTransform.</param>
        /// <param name="computedUvOffset">The UV offset to set into the LightmapUVOffset uniform of your LightmappedBitmap material.</param>
        public void ComputeViewPositionAndUVOffset (
            Vector2 viewPosition,
            int lightmapWidth, int lightmapHeight,
            out Vector2 computedViewPosition, 
            out Vector2 computedUvOffset
        ) {
            var truncated = new Vector2(
                (float)Math.Truncate(viewPosition.X * Configuration.RenderScale.X),
                (float)Math.Truncate(viewPosition.Y * Configuration.RenderScale.Y)
            );
            computedViewPosition = truncated / Configuration.RenderScale;
            var offsetInPixels = (viewPosition - computedViewPosition);
            var offsetInLightmapTexels = offsetInPixels * Configuration.RenderScale;
            computedUvOffset = offsetInLightmapTexels / new Vector2(lightmapWidth, lightmapHeight);
        }
    }

    public class RendererConfiguration {
        // The maximum width and height of the viewport.
        public readonly Pair<int>    MaximumRenderSize;

        // Uses a high-precision g-buffer and internal lightmap.
        public readonly bool         HighQuality;
        // Generates downscaled versions of the internal lightmap that the
        //  renderer can use to estimate the brightness of the scene for HDR.
        public readonly bool         EnableBrightnessEstimation;

        // Scales world coordinates when rendering the G-buffer and lightmap
        public Vector2 RenderScale                 = Vector2.One;

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
        public float DistanceFieldMaxConeRadius           = 24;
        public float DistanceFieldConeGrowthFactor        = 1.0f;
        // The maximum number of distance field slices to update per frame.
        // Setting this value too high can crash your video driver.
        public int   DistanceFieldUpdateRate              = 1;
        public float DistanceFieldOcclusionToOpacityPower = 1;

        // The current width and height of the viewport.
        // Must not be larger than MaximumRenderSize.
        public Pair<int> RenderSize;

        // Sets the default ramp texture to use for lights with no ramp texture set.
        public Texture2D DefaultRampTexture;

        public RendererConfiguration (
            int maxWidth, int maxHeight, bool highQuality,
            bool enableBrightnessEstimation = false
        ) {
            HighQuality = highQuality;
            MaximumRenderSize = new Pair<int>(maxWidth, maxHeight);
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

    public enum VisualizationMode {
        Surfaces,
        Outlines
    }

    public struct VisualizationInfo {
        public Vector3 ViewCenter;
        public Vector3 Up, Right;
    }

    public class LightSorter : IComparer<LightSource> {
        public static readonly LightSorter Instance = new LightSorter();

        public int Compare (LightSource x, LightSource y) {
            int xTexID = 0, yTexID = 0;
            if (x.RampTexture != null)
                xTexID = x.RampTexture.GetHashCode();
            if (y.RampTexture != null)
                yTexID = y.RampTexture.GetHashCode();
            var result = xTexID.CompareTo(yTexID);
            if (result == 0)
                result = x.TypeID.CompareTo(y.TypeID);
            return result;
        }
    }
}
