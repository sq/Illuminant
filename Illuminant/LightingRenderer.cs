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
        private class HeightVolumeCacheData : IDisposable {
            public Texture2D VertexDataTexture;
            public readonly HeightVolumeVertex[] BoundingBoxVertices = new HeightVolumeVertex[4];

            public void Dispose () {
                VertexDataTexture.Dispose();
            }
        }

        private class LightTypeRenderStateKeyComparer : IEqualityComparer<LightTypeRenderStateKey> {
            public static readonly LightTypeRenderStateKeyComparer Instance = new LightTypeRenderStateKeyComparer();

            public bool Equals (LightTypeRenderStateKey x, LightTypeRenderStateKey y) {
                return x.Equals(y);
            }

            public int GetHashCode (LightTypeRenderStateKey obj) {
                return obj.GetHashCode();
            }
        }

        private struct LightTypeRenderStateKey {
            public LightSourceTypeID       Type;
            public Texture2D               RampTexture;
            public RendererQualitySettings Quality;
            public bool                    DistanceRamp;
            public object                  UniqueObject;

            public override int GetHashCode () {
                var result = ((int)Type) ^ (DistanceRamp ? 2057 : 16593);
                if (RampTexture != null)
                    result ^= RampTexture.GetHashCode();
                if (Quality != null)
                    result ^= Quality.GetHashCode();
                if (UniqueObject != null)
                    result ^= UniqueObject.GetHashCode();
                return result;
            }

            public override bool Equals (object obj) {
                if (obj is LightTypeRenderStateKey)
                    return Equals((LightTypeRenderStateKey)obj);

                return false;
            }

            public bool Equals (LightTypeRenderStateKey ltrsk) {
                return (UniqueObject == ltrsk.UniqueObject) &&
                    (Type == ltrsk.Type) &&
                    (DistanceRamp == ltrsk.DistanceRamp) &&
                    (RampTexture == ltrsk.RampTexture) &&
                    (Quality == ltrsk.Quality);
            }
        }

        private class LightTypeRenderState : IDisposable {
            public  readonly LightingRenderer           Parent;
            public  readonly LightTypeRenderStateKey    Key;
            public  readonly object                     Lock = new object();
            public  readonly UnorderedList<LightVertex> LightVertices = null;
            public  readonly Material                   Material, ProbeMaterial;

            private int                                 CurrentVertexCount = 0;
            private DynamicVertexBuffer                 LightVertexBuffer = null;

            public LightTypeRenderState (LightingRenderer parent, LightTypeRenderStateKey key) {
                Parent = parent;
                Key    = key;

                if (key.Type == LightSourceTypeID.Particle)
                    ;
                else
                    LightVertices = new UnorderedList<LightVertex>(512);

                switch (key.Type) {
                    case LightSourceTypeID.Sphere:
                        Material = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.SphereLight
                            : (
                                key.DistanceRamp
                                    ? parent.IlluminantMaterials.SphereLightWithDistanceRamp
                                    : parent.IlluminantMaterials.SphereLightWithOpacityRamp
                            );
                        ProbeMaterial = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.SphereLightProbe
                            : (
                                key.DistanceRamp
                                    ? parent.IlluminantMaterials.SphereLightProbeWithDistanceRamp
                                    : parent.IlluminantMaterials.SphereLightProbeWithOpacityRamp
                            );
                        break;
                    case LightSourceTypeID.Directional:
                        Material = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.DirectionalLight
                            : parent.IlluminantMaterials.DirectionalLightWithRamp;
                        ProbeMaterial = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.DirectionalLightProbe
                            : parent.IlluminantMaterials.DirectionalLightProbeWithRamp;
                        break;
                    case LightSourceTypeID.Particle:
                        // FIXME
                        if (key.RampTexture != null)
                            throw new NotImplementedException("Ramp textures");
                        Material = parent.IlluminantMaterials.ParticleSystemSphereLight;
                        if (parent.Configuration.EnableGlobalIllumination)
                            throw new NotImplementedException("GI");
                        ProbeMaterial = null;
                        break;
                    default:
                        throw new NotImplementedException(key.Type.ToString());
                }
            }

            private ParticleLightSource ParticleLightSource {
                get {
                    return Key.UniqueObject as ParticleLightSource;
                }
            }

            public int Count {
                get {
                    return LightVertices != null ? LightVertices.Count : ParticleLightSource.System.LiveCount;
                }
            }

            public int Capacity {
                get {
                    return LightVertices != null ? LightVertices.Capacity : ParticleLightSource.System.LiveCount;
                }
            }

            public void UpdateVertexBuffer () {
                lock (Lock) {
                    if (LightVertices == null)
                        return;

                    var vertexCount = Count;
                    var vertexCapacity = Capacity;

                    if ((LightVertexBuffer != null) && (LightVertexBuffer.VertexCount < vertexCount)) {
                        Parent.Coordinator.DisposeResource(LightVertexBuffer);
                        LightVertexBuffer = null;
                    }

                    if (LightVertexBuffer == null) {
                        LightVertexBuffer = new DynamicVertexBuffer(
                            Parent.Coordinator.Device, typeof(LightVertex),
                            vertexCapacity, BufferUsage.WriteOnly
                        );
                    }

                    if (vertexCount > 0) {
                        LightVertexBuffer.SetData(LightVertices.GetBuffer(), 0, vertexCount, SetDataOptions.Discard);
                    }

                    CurrentVertexCount = vertexCount;
                }
            }

            public DynamicVertexBuffer GetVertexBuffer () {
                lock (Lock) {
                    var vertexCount = Count;

                    if ((LightVertexBuffer == null) || CurrentVertexCount != vertexCount)
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

        public const int MaximumLightCount = 4096;
        public const int PackedSliceCount = 3;
        public const int DistanceFunctionBufferInitialSize = 256;

        const int        DistanceLimit = 520;
        
        public  readonly RenderCoordinator    Coordinator;

        public  readonly DefaultMaterialSet   Materials;
        public           IlluminantMaterials  IlluminantMaterials { get; private set; }

        public  readonly LightProbeCollection Probes;

        public  readonly DepthStencilState TopFaceDepthStencilState, FrontFaceDepthStencilState;
        public  readonly DepthStencilState DistanceInteriorStencilState, DistanceExteriorStencilState;
        public  readonly DepthStencilState NeutralDepthStencilState;

        private readonly IndexBuffer         QuadIndexBuffer;

        class DistanceFunctionBuffer {
            public readonly LightingRenderer Renderer;
            public DynamicVertexBuffer VertexBuffer;
            public DistanceFunctionVertex[] Vertices;
            public int[] FirstOffset, PrimCount;
            public bool IsDirty;

            public DistanceFunctionBuffer (LightingRenderer renderer, int initialSize) {
                Renderer = renderer;
                Vertices = new DistanceFunctionVertex[initialSize];
                IsDirty = true;
                var numTypes = (int)LightObstructionType.MAX + 1;
                FirstOffset = new int[numTypes];
                PrimCount   = new int[numTypes];
            }

            public void EnsureSize (int size) {
                if (Vertices.Length > size)
                    return;

                var actualSize = ((size + 127) / 128) * 128;

                Vertices = new DistanceFunctionVertex[actualSize];
                IsDirty = true;
            }

            public void EnsureVertexBuffer () {
                if ((VertexBuffer != null) && (VertexBuffer.VertexCount < Vertices.Length)) {
                    Renderer.Coordinator.DisposeResource(VertexBuffer);
                    VertexBuffer = null;
                }

                if (VertexBuffer == null) {
                    lock (Renderer.Coordinator.CreateResourceLock)
                        VertexBuffer = new DynamicVertexBuffer(Renderer.Coordinator.Device, typeof(DistanceFunctionVertex), Vertices.Length, BufferUsage.WriteOnly);

                    IsDirty = true;
                }
            }

            public void Flush () {
                if (!IsDirty)
                    return;

                VertexBuffer.SetData(Vertices, 0, Vertices.Length, SetDataOptions.Discard);

                IsDirty = false;
            }
        }

        private readonly DistanceFunctionBuffer StaticDistanceFunctions, DynamicDistanceFunctions;

        private readonly Dictionary<Polygon, HeightVolumeCacheData> HeightVolumeCache = 
            new Dictionary<Polygon, HeightVolumeCacheData>(new ReferenceComparer<Polygon>());

        private readonly UnorderedList<Billboard> BillboardScratch = new UnorderedList<Billboard>();
        private readonly UnorderedList<BillboardVertex> BillboardVertexScratch = new UnorderedList<BillboardVertex>();

        private readonly BufferRing _Lightmaps;

        private DistanceField _DistanceField;
        private GBuffer _GBuffer;

        private readonly BufferRing _LuminanceBuffers;
        private readonly object     _LuminanceReadbackArrayLock = new object();
        private          float[]    _LuminanceReadbackArray;

        private readonly Action<DeviceManager, object>
            BeginLightPass, EndLightPass, EndLightProbePass,
            IlluminationBatchSetup, LightProbeBatchSetup,
            GIProbeBatchSetup, EndGIProbePass,
            ParticleLightBatchSetup;

        // FIXME: Thread sync issue?
        private Vector2? PendingDrawViewportPosition, PendingDrawViewportScale;
        private Vector2? PendingFieldViewportPosition, PendingFieldViewportScale;

        private readonly PrimitiveBeforeDraw<BillboardVertex> SetTextureForGBufferBillboard;

        private readonly object _LightStateLock = new object();
        private readonly Dictionary<LightTypeRenderStateKey, LightTypeRenderState> LightRenderStates = 
            new Dictionary<LightTypeRenderStateKey, LightTypeRenderState>(LightTypeRenderStateKeyComparer.Instance);

        public readonly RendererConfiguration Configuration;
        public LightingEnvironment Environment;

        private static readonly short[] QuadIndices = new short[] {
            0, 1, 3, 1, 2, 3
        };

        private Uniforms.Environment EnvironmentUniforms;

        private string _Name;

        public LightingRenderer (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, LightingEnvironment environment,
            RendererConfiguration configuration
        ) {
            Materials = materials;
            Coordinator = coordinator;
            Configuration = configuration;

            IlluminantMaterials = new IlluminantMaterials(materials);

            _GIBounces = new GIBounce[Configuration.MaximumGIBounceCount];

            BeginLightPass                = _BeginLightPass;
            EndLightPass                  = _EndLightPass;
            EndLightProbePass             = _EndLightProbePass;
            EndGIProbePass                = _EndGIProbePass;
            IlluminationBatchSetup        = _IlluminationBatchSetup;
            LightProbeBatchSetup          = _LightProbeBatchSetup;
            GIProbeBatchSetup             = _GIProbeBatchSetup;
            ParticleLightBatchSetup       = _ParticleLightBatchSetup;
            SetTextureForGBufferBillboard = _SetTextureForGBufferBillboard;

            lock (coordinator.CreateResourceLock) {
                QuadIndexBuffer = new IndexBuffer(
                    coordinator.Device, IndexElementSize.SixteenBits, MaximumLightCount * 6, BufferUsage.WriteOnly
                );
                FillIndexBuffer();
            }

            DynamicDistanceFunctions = new DistanceFunctionBuffer(this, DistanceFunctionBufferInitialSize);
            StaticDistanceFunctions  = new DistanceFunctionBuffer(this, DistanceFunctionBufferInitialSize);

            _Lightmaps = new BufferRing(
                coordinator,
                Configuration.MaximumRenderSize.First, 
                Configuration.MaximumRenderSize.Second,
                false,
                Configuration.HighQuality
                    ? SurfaceFormat.Rgba64
                    : SurfaceFormat.Color,
                Configuration.RingBufferSize
            );

            lock (Coordinator.CreateResourceLock) {
                _LightProbePositions = new Texture2D(
                    coordinator.Device,
                    Configuration.MaximumLightProbeCount,
                    1, false, SurfaceFormat.Vector4
                );
                _LightProbeNormals = new Texture2D(
                    coordinator.Device,
                    Configuration.MaximumLightProbeCount,
                    1, false, SurfaceFormat.Vector4
                );
            }

            _LightProbeValueBuffers = new BufferRing(
                coordinator,
                Configuration.MaximumLightProbeCount, 
                1,
                false,
                SurfaceFormat.HdrBlendable,
                Configuration.RingBufferSize
            );

            if (Configuration.EnableGlobalIllumination)
                CreateGIProbeResources();

            if (Configuration.EnableBrightnessEstimation) {
                var width = Configuration.MaximumRenderSize.First / 2;
                var height = Configuration.MaximumRenderSize.Second / 2;

                _LuminanceBuffers = new BufferRing(coordinator, width, height, true, SurfaceFormat.Single, Configuration.RingBufferSize);
            }

            EnsureGBuffer();

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
            
            NeutralDepthStencilState = new DepthStencilState {
                StencilEnable = false,
                DepthBufferEnable = false
            };

            LoadMaterials(content);

            Environment = environment;
            Probes = new LightProbeCollection(Configuration.MaximumLightProbeCount);

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
            /*
            if (_Lightmap != null)
                _Lightmap.SetName(ObjectNames.ToObjectID(this) + ":Lightmap");
            foreach (var lb in _LuminanceBuffers)
                lb.SetName(ObjectNames.ToObjectID(this) + ":Luminance");
            */

            if (_GBuffer != null)
                _GBuffer.Texture.SetName(ObjectNames.ToObjectID(this) + ":GBuffer");
        }

        public DistanceField DistanceField {
            get {
                return _DistanceField;
            }
            set {
                _DistanceField = value;
            }
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            Environment.GIVolumes.IsDirty = true;
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
            Coordinator.DisposeResource(_DistanceField);
            Coordinator.DisposeResource(_GBuffer);
            Coordinator.DisposeResource(_Lightmaps);
            Coordinator.DisposeResource(_LuminanceBuffers);
            Coordinator.DisposeResource(_LightProbePositions);
            Coordinator.DisposeResource(_LightProbeNormals);
            Coordinator.DisposeResource(_LightProbeValueBuffers);

            ReleaseGIProbeResources();

            foreach (var kvp in HeightVolumeCache)
                Coordinator.DisposeResource(kvp.Value);

            /*
            foreach (var m in LoadedMaterials)
                Coordinator.DisposeResource(m);
            */
        }

        public RenderTarget2D GetLightmap (bool allowBlocking) {
            long temp;
            return _Lightmaps.GetBuffer(allowBlocking, out temp);
        }

        private void ComputeUniforms () {
            EnvironmentUniforms = new Uniforms.Environment {
                GroundZ = Environment.GroundZ,
                ZToYMultiplier = Configuration.TwoPointFiveD
                    ? Environment.ZToYMultiplier
                    : 0.0f,
                InvZToYMultiplier = Configuration.TwoPointFiveD
                    ? (Environment.ZToYMultiplier > 0) ? (1f / Environment.ZToYMultiplier) : 0
                    : 0.0f,
                RenderScale = Configuration.RenderScale
            };
        }

        private void PushLightingViewTransform (RenderTarget2D renderTarget) {
            var vt = ViewTransform.CreateOrthographic(
                renderTarget.Width, renderTarget.Height
            );

            var coordOffset = new Vector2(1.0f / Configuration.RenderScale.X, 1.0f / Configuration.RenderScale.Y) * 0.5f;

            vt.Position = PendingDrawViewportPosition.GetValueOrDefault(Materials.ViewportPosition);
            vt.Scale = PendingDrawViewportScale.GetValueOrDefault(Materials.ViewportScale);

            if (Configuration.ScaleCompensation)
                vt.Position += coordOffset;

            Materials.PushViewTransform(ref vt);
        }

        private void _BeginLightPass (DeviceManager device, object userData) {
            var buffer = (RenderTarget2D)userData;

            device.Device.Viewport = new Viewport(0, 0, buffer.Width, buffer.Height);

            device.PushStates();
            PushLightingViewTransform(buffer);
        }

        private void _EndLightPass (DeviceManager device, object userData) {
            Materials.PopViewTransform();
            device.PopStates();

            var buffer = (RenderTarget2D)userData;
            _Lightmaps.MarkRenderComplete(buffer);
        }

        private void _IlluminationBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;
            lock (_LightStateLock)
                ltrs.UpdateVertexBuffer();

            device.Device.BlendState = RenderStates.AdditiveBlend;

            SetLightShaderParameters(ltrs.Material, ltrs.Key.Quality);
            var p = ltrs.Material.Effect.Parameters;
            var rampTexture = p["RampTexture"];
            if (rampTexture != null)
                rampTexture.SetValue(ltrs.Key.RampTexture);
        }

        private void _ParticleLightBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;
            var pls = (ParticleLightSource)ltrs.Key.UniqueObject;
            IlluminationBatchSetup (device, ltrs);
            var p = ltrs.Material.Effect.Parameters;
            var lightSource = pls.Template;
            p["LightProperties"].SetValue(new Vector4(
                lightSource.Radius,
                lightSource.RampLength,
                (int)lightSource.RampMode,
                lightSource.CastsShadows ? 1f : 0f
            ));
            p["MoreLightProperties"].SetValue(new Vector4(
                lightSource.AmbientOcclusionRadius,
                lightSource.ShadowDistanceFalloff.GetValueOrDefault(-99999),
                lightSource.FalloffYFactor,
                lightSource.AmbientOcclusionOpacity
            ));
            p["LightColor"].SetValue(lightSource.Color);
        }

        private void SetLightShaderParameters (Material material, RendererQualitySettings q) {
            var effect = material.Effect;
            var p = effect.Parameters;

            SetGBufferParameters(p);

            var ub = Materials.GetUniformBinding<Uniforms.Environment>(material, "Environment");
            ub.Value.Current = EnvironmentUniforms;

            SetDistanceFieldParameters(material, true, q);
        }

        private LightTypeRenderState GetLightRenderState (LightSource ls) {
            var ltk =
                new LightTypeRenderStateKey {
                    Type = ls.TypeID,
                    RampTexture = ls.RampTexture ?? Configuration.DefaultRampTexture,
                    Quality = ls.Quality ?? Configuration.DefaultQuality,
                    UniqueObject = (ls is ParticleLightSource) ? ls : null
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

        private BufferRing.InProgressRender UpdateLuminanceBuffer (
            IBatchContainer container, int layer,
            RenderTarget2D lightmap, 
            float intensityScale
        ) {
            var newLuminanceBuffer = _LuminanceBuffers.BeginDraw(true);
            if (!newLuminanceBuffer)
                throw new Exception("Failed to get luminance buffer");

            var w = Configuration.RenderSize.First / 2;
            var h = Configuration.RenderSize.Second / 2;
            using (var copyGroup = BatchGroup.ForRenderTarget(
                container, layer, newLuminanceBuffer.Buffer,
                (dm, _) => {
                    dm.Device.Viewport = new Viewport(0, 0, w, h);
                    Materials.PushViewTransform(ViewTransform.CreateOrthographic(w, h));
                },
                (dm, _) => {
                    Materials.PopViewTransform();
                    // FIXME: Maybe don't do this until Present?
                    newLuminanceBuffer.Dispose();
                }
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(copyGroup, -1, "LightingRenderer {0} : Generate HDR Buffer", this.ToObjectID());

                var ir = new ImperativeRenderer(copyGroup, Materials);
                var m = IlluminantMaterials.CalculateLuminance;
                ir.Clear(color: Color.Transparent);
                ir.Draw(
                    lightmap, 
                    new Rectangle(0, 0, w, h), 
                    new Rectangle(0, 0, Configuration.RenderSize.First, Configuration.RenderSize.Second), 
                    material: m
                );
            }

            // FIXME: Wait for valid data?
            return newLuminanceBuffer;
        }

        /// <summary>
        /// Updates the lightmap in the target batch container on the specified layer.
        /// To display lighting, call RenderedLighting.Resolve on the result.
        /// </summary>
        /// <param name="container">The batch container to render lighting into.</param>
        /// <param name="layer">The layer to render lighting into.</param>
        /// <param name="intensityScale">A factor to scale the intensity of all light sources. You can use this to rescale the intensity of light values for HDR.</param>
        /// <param name="paintDirectIllumination">If false, direct illumination will not be rendered (only light probes will be updated).</param>
        /// <param name="indirectIlluminationSettings">If specified, indirect illumination is rendered based on the provided settings.</param>
        public RenderedLighting RenderLighting (
            IBatchContainer container, int layer, 
            float intensityScale = 1.0f, 
            bool paintDirectIllumination = true,
            GIRenderSettings indirectIlluminationSettings = null,
            Vector2? viewportPosition = null,
            Vector2? viewportScale = null
        ) {
            var lightmap = _Lightmaps.BeginDraw(true);
            var lightProbe = default(BufferRing.InProgressRender);

            if (Probes.Count > 0) {
                long lastProbesTimestamp;
                var lastProbes = _LightProbeValueBuffers.GetBuffer(false, out lastProbesTimestamp);
                if (lastProbes != null) {
                    var q = Coordinator.ThreadGroup.GetQueueForType<LightProbeDownloadTask>();
                    q.Enqueue(new LightProbeDownloadTask {
                        Renderer = this,
                        ScaleFactor = 1.0f / intensityScale,
                        Texture = lastProbes,
                        Timestamp = lastProbesTimestamp
                    });
                }

                lightProbe = _LightProbeValueBuffers.BeginDraw(true);
            }

            /*
            lock (_GIProbes)
            if (_GIProbes.Count > 0) {
                var q = Coordinator.ThreadGroup.GetQueueForType<GIProbeDownloadTask>();
                q.Enqueue(new GIProbeDownloadTask {
                    Renderer = this,
                    ScaleFactor = 1.0f / intensityScale
                });
            }
            */

            var result = new RenderedLighting(
                this, lightmap.Buffer, 1.0f / intensityScale,
                lightProbe.Buffer
            );

            int layerIndex = 0;

            ComputeUniforms();

            PendingDrawViewportPosition = viewportPosition;
            PendingDrawViewportScale = viewportScale;

            using (var outerGroup = BatchGroup.New(container, layer)) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(outerGroup, -9999, "LightingRenderer {0} : Begin", this.ToObjectID());

                // HACK: We make a copy of the previous lightmap so that brightness estimation can read it, without
                //  stalling on the current lightmap being rendered
                if (Configuration.EnableBrightnessEstimation) {
                    var q = Coordinator.ThreadGroup.GetQueueForType<HistogramUpdateTask>();
                    long temp;
                    // q.WaitUntilDrained();

                    var mostRecentLightmap = _Lightmaps.GetBuffer(false, out temp);
                    if (mostRecentLightmap != null)
                        result.LuminanceBuffer = UpdateLuminanceBuffer(outerGroup, 0, mostRecentLightmap, intensityScale).Buffer;
                }

                using (var resultGroup = BatchGroup.ForRenderTarget(
                    outerGroup, 1, lightmap.Buffer, 
                    before: BeginLightPass, after: EndLightPass,
                    userData: lightmap.Buffer
                )) {
                    ClearBatch.AddNew(
                        resultGroup, -1, Materials.Clear, new Color(0, 0, 0, Configuration.RenderGroundPlane ? 1f : 0f)
                    );

                    // TODO: Use threads?
                    lock (_LightStateLock) {
                        foreach (var kvp in LightRenderStates) {
                            if (kvp.Value.LightVertices != null)
                                kvp.Value.LightVertices.Clear();
                        }

                        using (var buffer = BufferPool<LightSource>.Allocate(Environment.Lights.Count)) {
                            Environment.Lights.CopyTo(buffer.Data);
                            Squared.Util.Sort.FastCLRSort(
                                buffer.Data, LightSorter.Instance, 0, Environment.Lights.Count
                            );

                            for (var i = 0; i < Environment.Lights.Count; i++) {
                                var lightSource = buffer.Data[i];
                                var pointLightSource = lightSource as SphereLightSource;
                                var directionalLightSource = lightSource as DirectionalLightSource;
                                var particleLightSource = lightSource as ParticleLightSource;

                                var ltrs = GetLightRenderState(lightSource);

                                if (particleLightSource != null)
                                    continue;

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

                        if (paintDirectIllumination)
                        foreach (var kvp in LightRenderStates) {
                            var ltrs = kvp.Value;
                            var count = ltrs.Count / 4;

                            if (RenderTrace.EnableTracing)
                                RenderTrace.Marker(resultGroup, layerIndex++, "LightingRenderer {0} : Render {1} {2} light(s)", this.ToObjectID(), count, ltrs.Key.Type);

                            var pls = ltrs.Key.UniqueObject as ParticleLightSource;
                            if (pls != null) {
                                if (!pls.IsActive)
                                    continue;

                                using (var bg = BatchGroup.New(
                                    resultGroup, layerIndex++, ParticleLightBatchSetup, null, ltrs
                                )) {
                                    pls.System.Render(bg, 0, ltrs.Material, null, null, pls.StippleFactor);
                                }
                            } else {
                                if (count <= 0)
                                    continue;

                                using (var nb = NativeBatch.New(
                                    resultGroup, layerIndex++, ltrs.Material, IlluminationBatchSetup, userData: ltrs
                                )) {
                                    nb.Add(new NativeDrawCall(
                                        PrimitiveType.TriangleList,
                                        ltrs.GetVertexBuffer(), 0,
                                        QuadIndexBuffer, 0, 0, ltrs.Count, 0, ltrs.Count / 2
                                    ));
                                }
                            }
                        }

                        if (
                            Configuration.EnableGlobalIllumination && 
                            (GIProbeCount > 0) &&
                            (indirectIlluminationSettings != null) &&
                            (indirectIlluminationSettings.Brightness > 0)
                        )
                            RenderGlobalIllumination(
                                resultGroup, layerIndex++, 
                                indirectIlluminationSettings.Brightness, indirectIlluminationSettings.BounceIndex,
                                intensityScale
                            );
                    }
                }

                lock (_LightStateLock) {
                    if (Probes.Count > 0) {
                        if (Probes.IsDirty) {
                            UpdateLightProbeTexture();
                            Probes.IsDirty = false;
                        }
                        UpdateLightProbes(outerGroup, 3, lightProbe.Buffer, false, intensityScale);
                    }

                    UpdateGIProbes(outerGroup, 4, intensityScale);
                }

                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(outerGroup, 9999, "LightingRenderer {0} : End", this.ToObjectID());
            }

            return result;
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
            vertex.MoreLightProperties.W = lightSource.AmbientOcclusionOpacity;

            for (int i = 0; i < 4; i++) {
                vertex.Corner = vertex.Unused = (short)i;
                ltrs.LightVertices.Add(ref vertex);
            }
        }

        private void RenderDirectionalLightSource (DirectionalLightSource lightSource, float intensityScale, LightTypeRenderState ltrs) {
            LightVertex vertex;
            vertex.LightCenter = lightSource._Direction;
            vertex.Color = lightSource.Color;
            vertex.Color.W *= (lightSource.Opacity * intensityScale);
            vertex.LightProperties = new Vector4(
                lightSource.CastsShadows ? 1f : 0f,
                lightSource.ShadowTraceLength,
                lightSource.ShadowSoftness,
                lightSource.ShadowRampRate
            );
            vertex.MoreLightProperties = new Vector4(
                lightSource.AmbientOcclusionRadius,
                lightSource.ShadowDistanceFalloff.GetValueOrDefault(-99999),
                0,
                lightSource.AmbientOcclusionOpacity
            );

            var lightBounds = new Bounds(
                Vector2.Zero,
                new Vector2(
                    Configuration.MaximumRenderSize.First,
                    Configuration.MaximumRenderSize.Second
                )
            );

            for (int i = 0; i < 4; i++) {
                vertex.Corner = vertex.Unused = (short)i;
                ltrs.LightVertices.Add(ref vertex);
            }
        }

        /// <summary>
        /// Resolves the current lightmap into the specified batch container on the specified layer.
        /// </summary>
        /// <param name="container">The batch container to resolve lighting into.</param>
        /// <param name="layer">The layer to resolve lighting into.</param>
        private void ResolveLighting (
            IBatchContainer container, int layer,
            RenderTarget2D lightmap,
            float? width, float? height, 
            HDRConfiguration? hdr,
            bool resolveToSRGB
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
                var p = m.Effect.Parameters;

                SetGBufferParameters(p);
                p["InverseScaleFactor"].SetValue(
                    hdr.HasValue
                        ? ((hdr.Value.InverseScaleFactor != 0) ? hdr.Value.InverseScaleFactor : 1.0f)
                        : 1.0f
                );
                p["ResolveToSRGB"].SetValue(resolveToSRGB);

                var ub = Materials.GetUniformBinding<Uniforms.Environment>(m, "Environment");
                ub.Value.Current = EnvironmentUniforms;

                if (hdr.HasValue) {
                    if (hdr.Value.Mode == HDRMode.GammaCompress)
                        IlluminantMaterials.SetGammaCompressionParameters(
                            hdr.Value.GammaCompression.MiddleGray,
                            hdr.Value.GammaCompression.AverageLuminance,
                            hdr.Value.GammaCompression.MaximumLuminance,
                            hdr.Value.Offset
                        );
                    else if (hdr.Value.Mode == HDRMode.ToneMap)
                        IlluminantMaterials.SetToneMappingParameters(
                            hdr.Value.Exposure,
                            hdr.Value.ToneMapping.WhitePoint,
                            hdr.Value.Offset,
                            hdr.Value.Gamma
                        );
                    else 
                        IlluminantMaterials.SetToneMappingParameters(
                            hdr.Value.Exposure,
                            1f,
                            hdr.Value.Offset,
                            hdr.Value.Gamma
                        );
                }
            });

            var bounds = lightmap.BoundsFromRectangle(
                new Rectangle(0, 0, Configuration.RenderSize.First, Configuration.RenderSize.Second)
            );

            var dc = new BitmapDrawCall(
                lightmap, Vector2.Zero, bounds
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
            if (_DistanceField == null)
                return new VisualizationInfo();

            ComputeUniforms();

            var tl = new Vector3(rectangle.TopLeft, 0);
            var tr = new Vector3(rectangle.TopRight, 0);
            var bl = new Vector3(rectangle.BottomLeft, 0);
            var br = new Vector3(rectangle.BottomRight, 0);

            var extent = new Vector3(
                _DistanceField.VirtualWidth,
                _DistanceField.VirtualHeight,
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

                    SetDistanceFieldParameters(material, true, Configuration.DefaultQuality);

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

        private void SetDistanceFieldParameters (
            Material m, bool setDistanceTexture,
            RendererQualitySettings q
        ) {
            Uniforms.DistanceField dfu;
            var p = m.Effect.Parameters;

            Materials.TrySetBoundUniform(m, "Environment", ref EnvironmentUniforms);

            if (_DistanceField == null) {
                dfu = new Uniforms.DistanceField();
                dfu.Extent.Z = Environment.MaximumZ;
                Materials.TrySetBoundUniform(m, "DistanceField", ref dfu);
                if (setDistanceTexture)
                    p["DistanceFieldTexture"].SetValue((Texture2D)null);
                return;
            }

            if (q == null)
                q = Configuration.DefaultQuality;

            dfu = new Uniforms.DistanceField(_DistanceField, Environment.MaximumZ) {
                MaxConeRadius = q.MaxConeRadius,
                ConeGrowthFactor = q.ConeGrowthFactor,
                OcclusionToOpacityPower = q.OcclusionToOpacityPower,
                StepLimit = q.MaxStepCount,
                MinimumLength = q.MinStepSize,
                LongStepFactor = q.LongStepFactor
            };

            Materials.TrySetBoundUniform(m, "DistanceField", ref dfu);

            if (setDistanceTexture)
                p["DistanceFieldTexture"].SetValue(_DistanceField.Texture);

            p["MaximumEncodedDistance"].SetValue(_DistanceField.MaximumEncodedDistance);
        }

        public void InvalidateFields (
            bool invalidateGBuffer = true,
            bool invalidateDistanceField = true,
            bool rebuildGI = true
        ) {
            if (invalidateGBuffer) {
                EnsureGBuffer();
                if (_GBuffer != null)
                    _GBuffer.Invalidate();
            }

            if ((_DistanceField != null) && invalidateDistanceField)
                _DistanceField.Invalidate();

            // FIXME: rebuildGI only?
            Environment.GIVolumes.IsDirty = true;

            if (rebuildGI)
            foreach (var b in _GIBounces) {
                if (b != null)
                    b.Invalidate();
            }
        }

        public void UpdateFields (
            IBatchContainer container, int layer, 
            Vector2? viewportPosition = null, Vector2? viewportScale = null
        ) {
            EnsureGBuffer();

            ComputeUniforms();

            var viewportChanged = (PendingDrawViewportPosition != viewportPosition) || (PendingDrawViewportScale != viewportScale);

            PendingFieldViewportPosition = viewportPosition;
            PendingFieldViewportScale = viewportScale;

            if ((_GBuffer != null) && (!_GBuffer.IsValid || viewportChanged)) {
                var renderWidth = (int)(Configuration.MaximumRenderSize.First / Configuration.RenderScale.X);
                var renderHeight = (int)(Configuration.MaximumRenderSize.Second / Configuration.RenderScale.Y);

                RenderGBuffer(ref layer, container, renderWidth, renderHeight);

                if (Configuration.GBufferCaching)
                    _GBuffer.IsValid = true;
            }

            if ((_DistanceField != null) && _DistanceField.NeedsRasterize) {
                RenderDistanceField(ref layer, container);
            }
        }

        private Vector3 Extent3 {
            get {
                if (_DistanceField != null)
                    return _DistanceField.GetExtent3(Environment.MaximumZ);
                else
                    return new Vector3(0, 0, Environment.MaximumZ);
            }
        }

        private void RenderDistanceFieldPartition (ref int layerIndex, IBatchContainer resultGroup, bool? dynamicFlagFilter) {
            var ddf = _DistanceField as DynamicDistanceField;
            var sliceInfo = ((ddf != null) && (dynamicFlagFilter == false)) ? ddf.StaticSliceInfo : _DistanceField.SliceInfo;
            var renderTarget = ((ddf != null) && (dynamicFlagFilter == false)) ? ddf.StaticTexture : _DistanceField.Texture;

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
                    Materials.PushViewTransform(Materials.ViewTransform);
                },
                (dm, _) => {
                    Materials.PopViewTransform();
                }
            )) {
                // We incrementally do a partial update of the distance field.
                int layer = 0;

                BuildDistanceFieldDistanceFunctionBuffer(StaticDistanceFunctions, false);
                BuildDistanceFieldDistanceFunctionBuffer(DynamicDistanceFunctions, true);

                while (slicesToUpdate > 0) {
                    // FIXME
                    var slice = sliceInfo.InvalidSlices[0];
                    var physicalSlice = slice / PackedSliceCount;

                    RenderDistanceFieldSliceTriplet(
                        rtGroup, physicalSlice, slice, ref layer, dynamicFlagFilter
                    );

                    slicesToUpdate -= 3;
                }
            }
        }

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
            float sliceZ = (slice / Math.Max(1, (float)(_DistanceField.SliceCount - 1)));
            return sliceZ * Environment.MaximumZ;
        }

        private void RenderDistanceFieldSliceTriplet (
            BatchGroup rtGroup, int physicalSliceIndex, int firstVirtualSliceIndex, 
            ref int layer, bool? dynamicFlagFilter
        ) {
            var df = _DistanceField;
            var ddf = _DistanceField as DynamicDistanceField;

            var interior = IlluminantMaterials.DistanceFieldInterior;
            var exterior = IlluminantMaterials.DistanceFieldExterior;

            var sliceX = (physicalSliceIndex % df.ColumnCount) * df.SliceWidth;
            var sliceY = (physicalSliceIndex / df.ColumnCount) * df.SliceHeight;
            var sliceXVirtual = (physicalSliceIndex % df.ColumnCount) * df.VirtualWidth;
            var sliceYVirtual = (physicalSliceIndex / df.ColumnCount) * df.VirtualHeight;

            var viewTransform = ViewTransform.CreateOrthographic(
                0, 0, 
                (int)Math.Ceiling(df.VirtualWidth * df.ColumnCount), 
                (int)Math.Ceiling(df.VirtualHeight * df.RowCount)
            );
            viewTransform.Position = new Vector2(-sliceXVirtual, -sliceYVirtual);

            Action<DeviceManager, object> beginSliceBatch =
                (dm, _) => {
                    // TODO: Optimize this
                    dm.Device.ScissorRectangle = new Rectangle(
                        sliceX, sliceY, df.SliceWidth, df.SliceHeight
                    );

                    Materials.ApplyViewTransformToMaterial(IlluminantMaterials.ClearDistanceFieldSlice, ref viewTransform);
                    Materials.ApplyViewTransformToMaterial(interior, ref viewTransform);
                    Materials.ApplyViewTransformToMaterial(exterior, ref viewTransform);

                    SetDistanceFieldParameters(interior, false, Configuration.DefaultQuality);
                    SetDistanceFieldParameters(exterior, false, Configuration.DefaultQuality);

                    foreach (var m in IlluminantMaterials.DistanceFunctionTypes) {
                        Materials.ApplyViewTransformToMaterial(m, ref viewTransform);
                        SetDistanceFieldParameters(m, false, Configuration.DefaultQuality);
                    }
                };

            var lastVirtualSliceIndex = firstVirtualSliceIndex + 2;

            using (var group = BatchGroup.New(rtGroup, layer++,
                beginSliceBatch, null
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(group, -2, "LightingRenderer {0} : Begin Distance Field Slices [{1}-{2}]", this.ToObjectID(), firstVirtualSliceIndex, lastVirtualSliceIndex);

                ClearDistanceFieldSlice(
                    QuadIndices, group, -1, firstVirtualSliceIndex, dynamicFlagFilter == true ? ddf.StaticTexture : null
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
            using (var interiorGroup = BatchGroup.New(group, 2, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(interior, false, Configuration.DefaultQuality);
            }))
            using (var exteriorGroup = BatchGroup.New(group, 3, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
                dm.Device.DepthStencilState = DepthStencilState.None;
                SetDistanceFieldParameters(exterior, false, Configuration.DefaultQuality);
            }))
            foreach (var hv in Environment.HeightVolumes) {
                if ((dynamicFlagFilter != null) && (hv.IsDynamic != dynamicFlagFilter.Value))
                    continue;

                var p = hv.Polygon;
                var m = hv.Mesh3D;
                var b = hv.Bounds.Expand(DistanceLimit, DistanceLimit);
                var zRange = new Vector2(hv.ZBase, hv.ZBase + hv.Height);

                HeightVolumeCacheData cacheData;

                // FIXME: Handle position/zrange updates
                lock (HeightVolumeCache)
                    HeightVolumeCache.TryGetValue(p, out cacheData);

                if (cacheData == null) {
                    cacheData = new HeightVolumeCacheData();
                    
                    lock (Coordinator.CreateResourceLock)
                        cacheData.VertexDataTexture = new Texture2D(Coordinator.Device, p.Count, 1, false, SurfaceFormat.HalfVector4);

                    lock (Coordinator.UseResourceLock)
                    using (var vertices = BufferPool<HalfVector4>.Allocate(p.Count)) {
                        for (var j = 0; j < p.Count; j++) {
                            var edgeA = p[j];
                            var edgeB = p[Arithmetic.Wrap(j + 1, 0, p.Count - 1)];
                            vertices.Data[j] = new HalfVector4(
                                edgeA.X, edgeA.Y, edgeB.X, edgeB.Y
                            );
                        }

                        cacheData.VertexDataTexture.SetData(vertices.Data, 0, p.Count);
                    }

                    lock (HeightVolumeCache)
                        HeightVolumeCache[p] = cacheData;
                }

                cacheData.BoundingBoxVertices[0] = new HeightVolumeVertex(new Vector3(b.TopLeft, 0), Vector3.Up, zRange);
                cacheData.BoundingBoxVertices[1] = new HeightVolumeVertex(new Vector3(b.TopRight, 0), Vector3.Up, zRange);
                cacheData.BoundingBoxVertices[2] = new HeightVolumeVertex(new Vector3(b.BottomRight, 0), Vector3.Up, zRange);
                cacheData.BoundingBoxVertices[3] = new HeightVolumeVertex(new Vector3(b.BottomLeft, 0), Vector3.Up, zRange);

                // FIXME: Hoist these out and use BeforeDraw
                using (var batch = PrimitiveBatch<HeightVolumeVertex>.New(
                    interiorGroup, i, IlluminantMaterials.DistanceFieldInterior,
                    (dm, _) => {
                        var ep = interior.Effect.Parameters;
                        ep["NumVertices"].SetValue(p.Count);
                        ep["VertexDataTexture"].SetValue(cacheData.VertexDataTexture);
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
                    material.Effect.Parameters["ClearTexture"].SetValue(clearTexture);
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

        private void BuildDistanceFieldDistanceFunctionBuffer (DistanceFunctionBuffer result, bool? dynamicFlagFilter) {
            var items = Environment.Obstructions;

            var tl = new Vector3(0, 0, 0);
            var tr = new Vector3(_DistanceField.VirtualWidth, 0, 0);
            var br = new Vector3(_DistanceField.VirtualWidth, _DistanceField.VirtualHeight, 0);
            var bl = new Vector3(0, _DistanceField.VirtualHeight, 0);

            // HACK: Sort all the functions by type, fill the VB with each group,
            //  then issue a single draw for each
            using (var buffer = BufferPool<LightObstruction>.Allocate(items.Count))
            lock (result) {
                Array.Clear(result.FirstOffset, 0, result.FirstOffset.Length);
                Array.Clear(result.PrimCount, 0, result.PrimCount.Length);

                Array.Clear(buffer.Data, 0, buffer.Data.Length);
                items.CopyTo(buffer.Data);
                Array.Sort(buffer.Data, 0, items.Count, LightObstructionTypeComparer.Instance);

                result.IsDirty = true;
                result.EnsureSize(items.Count * 4);

                int j = 0;
                for (int i = 0; i < items.Count; i++) {
                    var item = buffer.Data[i];
                    var type = (int)item.Type;

                    if ((dynamicFlagFilter != null) && (item.IsDynamic != dynamicFlagFilter.Value))
                        continue;

                    if (result.FirstOffset[type] == -1)
                        result.FirstOffset[type] = j;

                    result.PrimCount[type] += 2;

                    // See definition of DISTANCE_MAX in DistanceFieldCommon.fxh
                    float offset = _DistanceField.MaximumEncodedDistance + 1;

                    tl = new Vector3(item.Center.X - item.Size.X - offset, item.Center.Y - item.Size.Y - offset, 0);
                    br = new Vector3(item.Center.X + item.Size.X + offset, item.Center.Y + item.Size.Y + offset, 0);
                    tr = new Vector3(br.X, tl.Y, 0);
                    bl = new Vector3(tl.X, br.Y, 0);

                    result.Vertices[j++] = new DistanceFunctionVertex(
                        tl, item.Center, item.Size
                    );
                    result.Vertices[j++] = new DistanceFunctionVertex(
                        tr, item.Center, item.Size
                    );
                    result.Vertices[j++] = new DistanceFunctionVertex(
                        br, item.Center, item.Size
                    );
                    result.Vertices[j++] = new DistanceFunctionVertex(
                        bl, item.Center, item.Size
                    );
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

            var numTypes = (int)LightObstructionType.MAX + 1;
            var batches  = new NativeBatch[numTypes];

            Action<DeviceManager, object> setup = null;

            for (int k = 0; k < 2; k++) {
                var dynamicFlag = (k != 0);
                if (dynamicFlagFilter.HasValue && dynamicFlagFilter.Value != dynamicFlag)
                    continue;

                var buffer = dynamicFlag ? StaticDistanceFunctions : DynamicDistanceFunctions;
                lock (buffer)
                for (int i = 0; i < numTypes; i++) {
                    if (buffer.PrimCount[i] <= 0)
                        continue;

                    var m = IlluminantMaterials.DistanceFunctionTypes[i];
                    if (RenderTrace.EnableTracing)
                        RenderTrace.Marker(group, (i * 2) + 3, "LightingRenderer {0} : Render {1}(s)", this.ToObjectID(), (LightObstructionType)i);

                    setup = (dm, _) => {
                        m.Effect.Parameters["SliceZ"].SetValue(sliceZ);

                        lock (buffer)
                            buffer.Flush();
                    };

                    using (var batch = NativeBatch.New(
                        group, (i * 2) + 4, m, setup
                    )) {
                        batch.Add(new NativeDrawCall(
                            PrimitiveType.TriangleList,
                            buffer.VertexBuffer, 0,
                            QuadIndexBuffer, buffer.FirstOffset[i], 0, buffer.PrimCount[i] * 2,
                            0, buffer.PrimCount[i]
                        ));
                    }
                }
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
            // HACK: Two conflicting approaches to fixing this
            Configuration.ScaleCompensation = false;

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

            var result = xTexID - yTexID;
            if (result == 0)
                result = x.TypeID - y.TypeID;
            return result;
        }
    }
}
