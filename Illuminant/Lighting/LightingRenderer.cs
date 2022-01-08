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
using Squared.Render.Resources;
using Squared.Render.Tracing;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private class HeightVolumeCacheData : IDisposable {
            public HeightVolumeBase Volume;
            public Texture2D VertexDataTexture;
            public readonly HeightVolumeVertex[] BoundingBoxVertices = new HeightVolumeVertex[4];

            public HeightVolumeCacheData (HeightVolumeBase volume) {
                Volume = volume;
            }

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
            public BlendState              BlendState;
            public RendererQualitySettings Quality;
            public ParticleLightSource     ParticleLightSource;
            public bool                    CastsShadows;

            public override int GetHashCode () {
                var result = (int)Type;
                if (BlendState != null)
                    result ^= BlendState.GetHashCode() << 2;
                if (RampTexture != null)
                    result ^= RampTexture.GetHashCode() << 4;
                if (Quality != null)
                    result ^= Quality.GetHashCode() << 6;
                if (ParticleLightSource != null)
                    result ^= ParticleLightSource.GetHashCode() << 8;
                return result;
            }

            public override bool Equals (object obj) {
                if (obj is LightTypeRenderStateKey ltrsk)
                    return Equals(ltrsk);

                return false;
            }

            public bool Equals (in LightTypeRenderStateKey ltrsk) {
                return (ParticleLightSource == ltrsk.ParticleLightSource) &&
                    (Type == ltrsk.Type) &&
                    (RampTexture == ltrsk.RampTexture) &&
                    (BlendState == ltrsk.BlendState) &&
                    (Quality == ltrsk.Quality) &&
                    (CastsShadows == ltrsk.CastsShadows);
            }
        }

        private class LightTypeRenderState : IDisposable {
            public static readonly DepthStencilState LightDepthStencilState =
                new DepthStencilState {
                    DepthBufferEnable = false,
                    StencilEnable = true,
                    StencilFail = StencilOperation.Keep,
                    StencilPass = StencilOperation.Keep,
                    StencilFunction = CompareFunction.Equal,
                    ReferenceStencil = 1,
                    StencilWriteMask = 0,
                    StencilDepthBufferFail = StencilOperation.Keep
                };

            public  readonly LightingRenderer           Parent;
            public  readonly LightTypeRenderStateKey    Key;
            public  readonly object                     Lock = new object();
            public  readonly UnorderedList<LightVertex> LightVertices = null;
            private          Material                   _Material, _ProbeMaterial;

            internal int                                LightCount = 0;
            private DynamicVertexBuffer                 LightVertexBuffer = null;
            private bool                                HadDistanceField;

            public LightTypeRenderState (LightingRenderer parent, LightTypeRenderStateKey key) {
                Parent = parent;
                Key    = key;

                if (key.Type != LightSourceTypeID.Particle)
                    LightVertices = new UnorderedList<LightVertex>(512);

                SelectMaterial();
            }

            public Material Material {
                get {
                    SelectMaterial();
                    return _Material;
                }
            }

            public Material ProbeMaterial {
                get {
                    SelectMaterial();
                    return _ProbeMaterial;
                }
            }

            private void SelectMaterial () {
                var hasDistanceField = Parent.DistanceField != null;

                if ((_Material != null) && (hasDistanceField == HadDistanceField))
                    return;

                var castsShadows = Key.CastsShadows && hasDistanceField;

                var key = Key;
                var parent = Parent;
                switch (key.Type) {
                    case LightSourceTypeID.Sphere:
                        _Material = (key.RampTexture == null)
                            ? (
                                !castsShadows
                                    ? parent.IlluminantMaterials.SphereLightWithoutDistanceField
                                    : parent.IlluminantMaterials.SphereLight
                            )
                            : (
                                parent.IlluminantMaterials.SphereLightWithDistanceRamp
                            );
                        _ProbeMaterial = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.SphereLightProbe
                            : (
                                parent.IlluminantMaterials.SphereLightProbeWithDistanceRamp
                            );
                        break;
                    case LightSourceTypeID.Directional:
                        _Material = (key.RampTexture == null)
                            ? ( 
                                castsShadows
                                ? parent.IlluminantMaterials.DirectionalLight
                                : parent.IlluminantMaterials.DirectionalLightWithoutDistanceField
                            )
                            : parent.IlluminantMaterials.DirectionalLightWithRamp;
                        _ProbeMaterial = (key.RampTexture == null)
                            ? parent.IlluminantMaterials.DirectionalLightProbe
                            : parent.IlluminantMaterials.DirectionalLightProbeWithRamp;
                        break;
                    case LightSourceTypeID.Particle:
                        // FIXME
                        if (key.RampTexture != null)
                            throw new NotImplementedException("Ramp textures");
                        _Material = castsShadows
                            ? parent.IlluminantMaterials.ParticleSystemSphereLight
                            : parent.IlluminantMaterials.ParticleSystemSphereLightWithoutDistanceField;
                        _ProbeMaterial = null;
                        break;
                    case LightSourceTypeID.Line:
                        _Material = parent.IlluminantMaterials.LineLight;
                        _ProbeMaterial = parent.IlluminantMaterials.LineLightProbe;
                        break;
                    case LightSourceTypeID.Projector:
                        _Material = castsShadows
                            ? parent.IlluminantMaterials.ProjectorLight
                            : parent.IlluminantMaterials.ProjectorLightWithoutDistanceField;
                        _ProbeMaterial = parent.IlluminantMaterials.ProjectorLightProbe;
                        break;
                    default:
                        throw new NotImplementedException(key.Type.ToString());
                }

                var blendState = Key.BlendState ?? BlendState.Additive;
                _Material = parent.Materials.Get(_Material, blendState: blendState, depthStencilState: LightDepthStencilState);
                if (_ProbeMaterial != null)
                    _ProbeMaterial = parent.Materials.Get(_ProbeMaterial, blendState: blendState, depthStencilState: DepthStencilState.None);

                if (_Material == null)
                    throw new Exception("No material found");

                HadDistanceField = hasDistanceField;
            }

            private ParticleLightSource ParticleLightSource {
                get {
                    return Key.ParticleLightSource as ParticleLightSource;
                }
            }

            public int Count {
                get {
                    return LightVertices != null ? LightCount : ParticleLightSource.System.LiveCount;
                }
            }

            public int VertexCount {
                get {
                    return LightVertices != null ? LightVertices.Count : 0;
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

                    var vertexCapacity = Capacity;

                    if ((LightVertexBuffer != null) && (LightVertexBuffer.VertexCount < vertexCapacity)) {
                        Parent.Coordinator.DisposeResource(LightVertexBuffer);
                        LightVertexBuffer = null;
                    }

                    if (LightVertexBuffer == null) {
                        LightVertexBuffer = new DynamicVertexBuffer(
                            Parent.Coordinator.Device, typeof(LightVertex),
                            vertexCapacity, BufferUsage.WriteOnly
                        );
                    }

                    if (vertexCapacity > 0)
                        LightVertexBuffer.SetData(LightVertices.GetBufferArray(), 0, vertexCapacity, SetDataOptions.Discard);
                }
            }

            public VertexBuffer GetCornerBuffer (bool forProbes) {
                switch (Key.Type) {
                    case LightSourceTypeID.Sphere:
                        return forProbes ? Parent.CornerBuffer : Parent.SphereBuffer;
                    default:
                        return Parent.CornerBuffer;
                }
            }

            public DynamicVertexBuffer GetVertexBuffer () {
                lock (Lock) {
                    if (LightVertexBuffer == null)
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

        private sealed class LightObstructionTypeComparer : IRefComparer<LightObstruction> {
            public static readonly LightObstructionTypeComparer Instance = 
                new LightObstructionTypeComparer();

            public int Compare (ref LightObstruction lhs, ref LightObstruction rhs) {
                if (rhs == null) {
                    return (lhs == null) ? 0 : -1;
                } else if (lhs == null) {
                    return 0;
                }

                return ((int)lhs.Type) - ((int)rhs.Type);
            }
        }        

        public const int PackedSliceCount = 3;
        public const int DistanceFunctionBufferInitialSize = 256;

        const int        DistanceLimit = 520;
        
        public  readonly RenderCoordinator    Coordinator;

        public  readonly DefaultMaterialSet   Materials;
        public           IlluminantMaterials  IlluminantMaterials { get; private set; }

        public  readonly LightProbeCollection Probes;

        public  readonly DepthStencilState TopFaceDepthStencilState, FrontFaceDepthStencilState;
        public  readonly DepthStencilState DistanceStencilState;

        private IndexBuffer         QuadIndexBuffer;
        private VertexBuffer        CornerBuffer;
        private VertexBuffer        SphereBuffer;

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
                var numTypes = (int)LightObstruction.MAX_Type + 1;
                FirstOffset = new int[numTypes];
                PrimCount   = new int[numTypes];
            }

            public void EnsureSize (int size) {
                if (Vertices.Length > size)
                    return;

                var actualSize = ((size + 4095) / 4096) * 4096;

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

        private BufferRing _Lightmaps;

        private DistanceField _DistanceField;
        private GBuffer _GBuffer;
        private Texture2D _DummyGBufferTexture, _DummyDistanceFieldTexture;

        private          BufferRing _LuminanceBuffers;
        private readonly object     _LuminanceReadbackArrayLock = new object();
        private          float[]    _LuminanceReadbackArray;

        private readonly Action<DeviceManager, object>
            BeginLightPass, EndLightPass, BeginLightProbePass, EndLightProbePass,
            IlluminationBatchSetup, LightProbeBatchSetup,
            ParticleLightBatchSetup, BeforeLuminanceBufferUpdate,
            BeforeRenderGBuffer, AfterRenderGBuffer,
            GBufferBillboardBatchSetup, AfterLuminanceBufferUpdate,
            BeginSliceBatch, BeginClearSliceBatch, EndSliceBatch;

        // FIXME: Thread sync issue?
        private Vector2? PendingDrawViewportPosition, PendingDrawViewportScale;
        private Vector2? PendingFieldViewportPosition, PendingFieldViewportScale;
        private float    PreviousZToYMultiplier;

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

        private readonly EffectProvider Effects;

        private readonly TypedUniform<Uniforms.DistanceField> uDistanceField;
        private readonly TypedUniform<Render.DitheringSettings> uDithering;

        private string _Name;

        public LightingRenderer (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, LightingEnvironment environment,
            RendererConfiguration configuration, IlluminantMaterials illuminantMaterials = null
        ) {
            Materials = materials;
            Coordinator = coordinator;
            Configuration = configuration;

            uDistanceField = materials.NewTypedUniform<Uniforms.DistanceField>("DistanceField");
            uDithering = materials.NewTypedUniform<Render.DitheringSettings>("Dithering");

            Effects = new EffectProvider(System.Reflection.Assembly.GetExecutingAssembly(), coordinator);

            IlluminantMaterials = illuminantMaterials ?? new IlluminantMaterials(materials);

            BeginLightPass                = _BeginLightPass;
            EndLightPass                  = _EndLightPass;
            BeginLightProbePass           = _BeginLightProbePass;
            EndLightProbePass             = _EndLightProbePass;
            IlluminationBatchSetup        = _IlluminationBatchSetup;
            LightProbeBatchSetup          = _LightProbeBatchSetup;
            ParticleLightBatchSetup       = _ParticleLightBatchSetup;
            SetTextureForGBufferBillboard = _SetTextureForGBufferBillboard;
            BeforeLuminanceBufferUpdate   = _BeforeLuminanceBufferUpdate;
            AfterLuminanceBufferUpdate    = _AfterLuminanceBufferUpdate;
            BeforeRenderGBuffer           = _BeforeRenderGBuffer;
            AfterRenderGBuffer            = _AfterRenderGBuffer;
            GBufferBillboardBatchSetup    = _GBufferBillboardBatchSetup;
            BeginSliceBatch               = _BeginSliceBatch;
            BeginClearSliceBatch          = _BeginClearSliceBatch;
            EndSliceBatch                 = _EndSliceBatch;

            InitBuffers(coordinator);

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
                Configuration.StencilCulling
                    ? DepthFormat.Depth24Stencil8
                    : DepthFormat.None,
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
                _DummyGBufferTexture = new Texture2D(coordinator.Device, 1, 1, false, SurfaceFormat.HalfVector4);
                _DummyDistanceFieldTexture = new Texture2D(coordinator.Device, 1, 1, false, DistanceField.Format);
            }

            _LightProbeValueBuffers = new BufferRing(
                coordinator,
                Configuration.MaximumLightProbeCount, 
                1,
                false,
                SurfaceFormat.HalfVector4,
                DepthFormat.None,
                Configuration.RingBufferSize
            );

            if (Configuration.EnableBrightnessEstimation) {
                var width = Configuration.MaximumRenderSize.First / 2;
                var height = Configuration.MaximumRenderSize.Second / 2;

                _LuminanceBuffers = new BufferRing(
                    coordinator, width, height, true, 
                    SurfaceFormat.Single, DepthFormat.None, 
                    Configuration.RingBufferSize
                );
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

            IlluminantMaterials.Load(coordinator, Effects);

            Environment = environment;
            Probes = new LightProbeCollection(Configuration.MaximumLightProbeCount);

            Coordinator.DeviceReset += Coordinator_DeviceReset;
        }

        private void InitBuffers (RenderCoordinator coordinator) {
            lock (coordinator.CreateResourceLock) {
                if (QuadIndexBuffer == null)
                    QuadIndexBuffer = new IndexBuffer(
                        coordinator.Device, IndexElementSize.SixteenBits, 6 * 6, BufferUsage.WriteOnly
                    );
                if (CornerBuffer == null)
                    CornerBuffer = new VertexBuffer(
                        coordinator.Device, typeof(CornerVertex), 4, BufferUsage.WriteOnly
                    );
                if (SphereBuffer == null)
                    SphereBuffer = new VertexBuffer(
                        coordinator.Device, typeof(CornerVertex), 12, BufferUsage.WriteOnly
                    );
            }

            lock (coordinator.UseResourceLock) {
                FillIndexBuffer();
                FillCornerBuffer();
                FillSphereBuffer();
            }
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
                // FIXME: This would be convenient, but it's possible you'd want to reuse an existing field unmodified? Maybe???
                // InvalidateFields();
            }
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            InitBuffers(Coordinator);
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

        private void FillCornerBuffer () {
            var buf = new CornerVertex[CornerBuffer.VertexCount];
            buf[0].CornerWeightsAndIndex = new Vector4(0, 0, 0, 0);
            buf[1].CornerWeightsAndIndex = new Vector4(1, 0, 0, 0);
            buf[2].CornerWeightsAndIndex = new Vector4(1, 1, 0, 0);
            buf[3].CornerWeightsAndIndex = new Vector4(0, 1, 0, 0);

            CornerBuffer.SetData(buf);
        }

        private void FillSphereBuffer () {
            const float cOne = 1.0f / 7.0f;
            const float mOne = 6.0f / 7.0f;

            var buf = new [] {
                new CornerVertex { CornerWeightsAndIndex = new Vector4( cOne, 0, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( mOne, 0, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( mOne, 1, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( cOne, 1, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( mOne, cOne, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( 1, cOne, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( 1, mOne, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( mOne, mOne, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( 0, cOne, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( cOne, cOne, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( cOne, mOne, 0, 0 ) },
                new CornerVertex { CornerWeightsAndIndex = new Vector4( 0, mOne, 0, 0 ) }
            };

            SphereBuffer.SetData(buf);
        }

        public void Dispose () {
            foreach (var kvp in LightRenderStates)
                kvp.Value.Dispose();

            Effects.Dispose();

            Coordinator.DisposeResource(ref _DummyGBufferTexture);
            Coordinator.DisposeResource(ref _DummyDistanceFieldTexture);
            Coordinator.DisposeResource(ref QuadIndexBuffer);
            Coordinator.DisposeResource(ref CornerBuffer);
            Coordinator.DisposeResource(ref SphereBuffer);
            Coordinator.DisposeResource(ref _DistanceField);
            Coordinator.DisposeResource(ref _GBuffer);
            Coordinator.DisposeResource(ref _Lightmaps);
            Coordinator.DisposeResource(ref _LuminanceBuffers);
            Coordinator.DisposeResource(ref _LightProbePositions);
            Coordinator.DisposeResource(ref _LightProbeNormals);
            Coordinator.DisposeResource(ref _LightProbeValueBuffers);

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
                MaximumZ = Environment.MaximumZ,
                ZToYMultiplier = Configuration.TwoPointFiveD
                    ? Environment.ZToYMultiplier
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
            vt.ResetZRanges();

            if (Configuration.ScaleCompensation)
                vt.Position += coordOffset;

            Materials.PushViewTransform(in vt);
        }

        private void _BeginLightPass (DeviceManager device, object userData) {
            var buffer = (RenderTarget2D)userData;

            var renderWidth = (int)(Configuration.MaximumRenderSize.First * Configuration.RenderScale.X);
            var renderHeight = (int)(Configuration.MaximumRenderSize.Second * Configuration.RenderScale.Y);

            device.PushStates();
            PushLightingViewTransform(buffer);

            device.Device.ScissorRectangle = new Rectangle(0, 0, Math.Min(renderWidth + 2, buffer.Width), Math.Min(renderHeight + 2, buffer.Height));

            device.Device.RasterizerState = RenderStates.ScissorOnly;
        }

        private void _BeginLightProbePass (DeviceManager device, object userData) {
            var buffer = (RenderTarget2D)userData;

            device.PushStates();
            PushLightingViewTransform(buffer);
        }

        private void _EndLightPass (DeviceManager device, object userData) {
            Materials.PopViewTransform();
            device.PopStates();

            var buffer = (RenderTarget2D)userData;
            device.Device.ScissorRectangle = new Rectangle(0, 0, buffer.Width, buffer.Height);

            _Lightmaps.MarkRenderComplete(buffer);
        }

        private void _IlluminationBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;
            lock (_LightStateLock)
                ltrs.UpdateVertexBuffer();

            SetLightShaderParameters(ltrs.Material, ltrs.Key.Quality);
            var p = ltrs.Material.Effect.Parameters;
            var rampTexture = p["RampTexture"];
            if (rampTexture != null)
                rampTexture.SetValue(ltrs.Key.RampTexture);
        }

        private void _ParticleLightBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;
            var pls = (ParticleLightSource)ltrs.Key.ParticleLightSource;
            IlluminationBatchSetup (device, ltrs);
            var p = ltrs.Material.Effect.Parameters;
            var lightSource = pls.Template;
            p["LightProperties"].SetValue(new Vector4(
                lightSource.Radius,
                lightSource.RampLength,
                (int)lightSource.RampMode,
                (lightSource.CastsShadows && (DistanceField != null)) ? 1f : 0f
            ));
            p["MoreLightProperties"].SetValue(new Vector4(
                lightSource.AmbientOcclusionOpacity > 0.001 ? lightSource.AmbientOcclusionRadius : 0,
                lightSource.ShadowDistanceFalloff.GetValueOrDefault(-99999),
                lightSource.FalloffYFactor,
                Arithmetic.Clamp(lightSource.AmbientOcclusionOpacity, 0f, 1f)
            ));
            p["LightColor"].SetValue(lightSource.Color);
        }

        private void SetLightShaderParameters (Material material, RendererQualitySettings q) {
            var effect = material.Effect;
            var p = effect.Parameters;

            SetGBufferParameters(p);

            EnvironmentUniforms.SetIntoParameters(p);

            SetDistanceFieldParameters(material, true, q);
        }

        private LightTypeRenderState GetLightRenderState (LightSource ls) {
            var ltk =
                new LightTypeRenderStateKey {
                    Type = ls.TypeID,
                    BlendState = ls.BlendMode,
                    RampTexture = ls.TextureRef ?? Configuration.DefaultRampTexture,
                    Quality = ls.Quality ?? Configuration.DefaultQuality,
                    ParticleLightSource = ls as ParticleLightSource,
                    CastsShadows = (DistanceField != null) && 
                    (
                        ls.CastsShadows || 
                        ((ls.AmbientOcclusionOpacity > 0) && (ls.AmbientOcclusionRadius > 0))
                    )
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

            LightTypeRenderState result;
            if (!LightRenderStates.TryGetValue(ltk, out result)) {
                LightRenderStates[ltk] = result = new LightTypeRenderState(
                    this, ltk
                );
            }

            return result;
        }

        private void _BeforeLuminanceBufferUpdate (DeviceManager dm, object userData) {
            int w, h;
            Configuration.GetRenderSize(out w, out h);
            w /= 2;
            h /= 2;
            dm.SetViewport(new Viewport(0, 0, w, h));
            Materials.PushViewTransform(ViewTransform.CreateOrthographic(w, h));
        }

        private void _AfterLuminanceBufferUpdate (DeviceManager dm, object userData) {
            Materials.PopViewTransform();
            var ipr = (BufferRing.InProgressRender)userData;
            // FIXME: Maybe don't do this until Present?
            ipr.Dispose();
        }

        private BufferRing.InProgressRender UpdateLuminanceBuffer (
            IBatchContainer container, int layer,
            RenderTarget2D lightmap, 
            float intensityScale
        ) {
            var newLuminanceBuffer = _LuminanceBuffers.BeginDraw(true);
            if (!newLuminanceBuffer)
                throw new Exception("Failed to get luminance buffer");

            int w, h;
            Configuration.GetRenderSize(out w, out h);
            w /= 2;
            h /= 2;

            var name = "Generate HDR Buffer";
            using (var copyGroup = BatchGroup.ForRenderTarget(
                container, layer, newLuminanceBuffer.Buffer,
                before: BeforeLuminanceBufferUpdate, 
                after: AfterLuminanceBufferUpdate,
                userData: newLuminanceBuffer,
                name: name,
                ignoreInvalidTargets: true
            )) {
                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(copyGroup, -1, "LightingRenderer {0} : {1}", this.ToObjectID(), name);

                var ir = new ImperativeRenderer(copyGroup, Materials);
                var m = IlluminantMaterials.CalculateLuminance;
                int w2, h2;
                Configuration.GetRenderSize(out w2, out h2);
                ir.Clear(color: Color.Transparent);
                ir.Draw(
                    lightmap, 
                    new Rectangle(0, 0, w, h), 
                    new Rectangle(0, 0, w2, h2),
                    material: m
                );
            }

            // FIXME: Wait for valid data?
            return newLuminanceBuffer;
        }

        // FIXME: This is awful
        private readonly HashSet<LightTypeRenderStateKey> DeadRenderStates = 
            new HashSet<LightTypeRenderStateKey>(LightTypeRenderStateKeyComparer.Instance);

        private readonly DirectionalLightSource DummyDirectionalLightForFullbrightMode = new DirectionalLightSource {
            Color = new Vector4(0, 0, 0, 1),
            CastsShadows = false
        };

        /// <summary>
        /// Updates the lightmap in the target batch container on the specified layer.
        /// To display lighting, call RenderedLighting.Resolve on the result.
        /// </summary>
        /// <param name="container">The batch container to render lighting into.</param>
        /// <param name="layer">The layer to render lighting into.</param>
        /// <param name="intensityScale">A factor to scale the intensity of all light sources. You can use this to rescale the intensity of light values for HDR.</param>
        /// <param name="paintDirectIllumination">If false, direct illumination will not be rendered (only light probes will be updated).</param>
        public RenderedLighting RenderLighting (
            IBatchContainer container, int layer, 
            float intensityScale = 1.0f, 
            bool paintDirectIllumination = true,
            Vector2? viewportPosition = null,
            Vector2? viewportScale = null
        ) {
            _Lightmaps.ResizeBuffers(Configuration.MaximumRenderSize.First, Configuration.MaximumRenderSize.Second);
            var lightmap = _Lightmaps.BeginDraw(true);
            var lightProbe = default(BufferRing.InProgressRender);

            if (_LuminanceBuffers != null) {
                var lwidth = Configuration.MaximumRenderSize.First / 2;
                var lheight = Configuration.MaximumRenderSize.Second / 2;

                // FIXME: This causes a crash
                _LuminanceBuffers.ResizeBuffers(lwidth, lheight);
            }

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

            foreach (var ls in Environment.Lights)
                ls.TextureRef?.EnsureInitialized(Configuration.RampTextureLoader);

            BatchGroup resultGroup;

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
                    if (
                        (mostRecentLightmap != null) &&
                        !mostRecentLightmap.IsDisposed && 
                        !mostRecentLightmap.IsContentLost 
                    ) {
                        result.LuminanceBuffer = UpdateLuminanceBuffer(outerGroup, 0, mostRecentLightmap, intensityScale).Buffer;
                    }
                }

                resultGroup = BatchGroup.ForRenderTarget(
                    outerGroup, 1, lightmap.Buffer,
                    before: BeginLightPass, after: EndLightPass,
                    userData: lightmap.Buffer,
                    name: "Light Pass",
                    ignoreInvalidTargets: true
                );

                {
                    var ambient = Environment.Ambient * intensityScale;
                    // Zero out the alpha value because we use it to indicate whether a pixel is fullbright
                    if (Configuration.AllowFullbright && Configuration.EnableGBuffer)
                        ambient.A = 0;

                    ClearBatch.AddNew(
                        resultGroup, -2, Materials.Clear, 
                        clearColor: ambient,
                        // If the g-buffer is disabled, initialize the whole lightmap so that it is already stencil selected
                        // When the g-buffer is enabled we will do a prepass to mark every pixel we want to light
                        clearStencil: Configuration.EnableGBuffer ? 0 : 1
                    );

                    if (Configuration.EnableGBuffer && Configuration.StencilCulling)
                        UpdateMaskFromGBuffer(resultGroup, -1);

                    // TODO: Use threads?
                    lock (_LightStateLock) {
                        foreach (var drs in DeadRenderStates) {
                            LightTypeRenderState ltrs;
                            if (!LightRenderStates.TryGetValue(drs, out ltrs))
                                continue;

                            LightRenderStates.Remove(drs);
                            Coordinator.DisposeResource(ltrs);
                        }

                        DeadRenderStates.Clear();

                        foreach (var kvp in LightRenderStates) {
                            if (kvp.Value.LightVertices != null)
                                kvp.Value.LightVertices.Clear();
                            kvp.Value.LightCount = 0;
                            DeadRenderStates.Add(kvp.Key);
                        }

                        using (var buffer = BufferPool<LightSource>.Allocate(Environment.Lights.Count)) {
                            Array.Clear(buffer.Data, 0, buffer.Data.Length);
                            Environment.Lights.CopyTo(buffer.Data);
                            Sort.FastCLRSortRef(
                                new ArraySegment<LightSource>(buffer.Data), LightSorter.Instance, 0, Environment.Lights.Count
                            );

                            // var renderedLights = new HashSet<LightSource>(new ReferenceComparer<LightSource>());

                            for (var i = 0; i < Environment.Lights.Count; i++) {
                                var lightSource = buffer.Data[i];
                                if (!lightSource.Enabled)
                                    continue;

                                var pointLightSource = lightSource as SphereLightSource;
                                var directionalLightSource = lightSource as DirectionalLightSource;
                                var particleLightSource = lightSource as ParticleLightSource;
                                var lineLightSource = lightSource as LineLightSource;
                                var projectorLightSource = lightSource as ProjectorLightSource;

                                /*
                                if (renderedLights.Contains(lightSource))
                                    throw new Exception("Duplicate light in Environment.Lights");
                                renderedLights.Add(lightSource);
                                */

                                var ltrs = GetLightRenderState(lightSource);
                                DeadRenderStates.Remove(ltrs.Key);

                                if (particleLightSource != null)
                                    continue;

                                if (pointLightSource != null)
                                    RenderSphereLightSource(pointLightSource, intensityScale, ltrs);
                                else if (directionalLightSource != null)
                                    RenderDirectionalLightSource(directionalLightSource, intensityScale, ltrs);
                                else if (lineLightSource != null)
                                    RenderLineLightSource(lineLightSource, intensityScale, ltrs);
                                else if (projectorLightSource != null)
                                    RenderProjectorLightSource(projectorLightSource, intensityScale, ltrs);
                                else
                                    throw new NotSupportedException(lightSource.GetType().Name);
                            };
                        }

                        // HACK: In fullbright mode, the lightmap will have an alpha value of 0 for any pixels
                        //  that have not been touched by a light source. Compensate by using a dummy directional light
                        //  to set the alpha value of any pixels that are not marked as fullbright by the g-buffer.
                        if (Configuration.AllowFullbright && Configuration.EnableGBuffer) {
                            var ltrs = GetLightRenderState(DummyDirectionalLightForFullbrightMode);
                            DeadRenderStates.Remove(ltrs.Key);
                            RenderDirectionalLightSource(DummyDirectionalLightForFullbrightMode, 1, ltrs);
                        }

                        foreach (var kvp in LightRenderStates)
                            kvp.Value.UpdateVertexBuffer();

                        if (paintDirectIllumination)
                        foreach (var kvp in LightRenderStates) {
                            if (DeadRenderStates.Contains(kvp.Key))
                                continue;

                            var ltrs = kvp.Value;
                            var count = ltrs.LightCount;

                            if (RenderTrace.EnableTracing)
                                RenderTrace.Marker(resultGroup, layerIndex++, "LightingRenderer {0} : Render {1} {2} light(s)", this.ToObjectID(), count, ltrs.Key.Type);

                            var pls = ltrs.Key.ParticleLightSource;
                            if (pls != null) {
                                if (!pls.Enabled)
                                    continue;

                                if (!pls.IsActive)
                                    continue;

                                using (var bg = BatchGroup.New(
                                    resultGroup, layerIndex++, ParticleLightBatchSetup, null, ltrs
                                )) {
                                    // FIXME: Single-frame delay between particle state and particle light source positions,
                                    //  due to some sort of race condition I can't figure out.
                                    pls.System.Render(
                                        bg, 0, ltrs.Material, null, null, 
                                        new Particles.ParticleRenderParameters {
                                            StippleFactor = pls.StippleFactor
                                        }, usePreviousData: true
                                    );
                                }
                            } else {
                                if (count <= 0)
                                    continue;

                                using (var nb = NativeBatch.New(
                                    resultGroup, layerIndex++, ltrs.Material, IlluminationBatchSetup, userData: ltrs
                                )) {
                                    var cornerBuffer = ltrs.GetCornerBuffer(false);
                                    // HACK: Split large numbers of lights into smaller draw operations
                                    const int step = 128;
                                    for (int i = 0; i < ltrs.LightCount; i += step) {
                                        int instanceCount = Math.Min(ltrs.LightCount - i, step);
                                        nb.Add(new NativeDrawCall(
                                            PrimitiveType.TriangleList,
                                            cornerBuffer, 0,
                                            ltrs.GetVertexBuffer(), i,
                                            null, 0,
                                            QuadIndexBuffer, 0, 0, cornerBuffer.VertexCount, 0, cornerBuffer.VertexCount / 2, instanceCount
                                        ));
                                    }
                                }
                            }
                        }
                    }
                }

                lock (_LightStateLock) {
                    // FIXME: If this is 1 as it was before, lighting breaks in lazy viewtransform mode
                    int baseLayer = 1;

                    if (Probes.Count > 0) {
                        if (Probes.IsDirty) {
                            UpdateLightProbeTexture();
                            Probes.IsDirty = false;
                        }
                        UpdateLightProbes(outerGroup, baseLayer, lightProbe.Buffer, intensityScale);
                    }
                }

                if (RenderTrace.EnableTracing)
                    RenderTrace.Marker(outerGroup, 9999, "LightingRenderer {0} : End", this.ToObjectID());
            }

            result.BatchGroup = resultGroup;
            return result;
        }

        private void RenderSphereLightSource (SphereLightSource lightSource, float intensityScale, LightTypeRenderState ltrs) {
            LightVertex vertex;
            vertex.LightPosition3 = vertex.LightPosition2 = vertex.LightPosition1 = new Vector4(lightSource.Position, 0);
            var color = lightSource.Color;
            color.W *= (lightSource.Opacity * intensityScale);
            vertex.Color2 = vertex.Color1 = color;
            vertex.LightProperties.X = lightSource.Radius;
            vertex.LightProperties.Y = lightSource.RampLength;
            vertex.LightProperties.Z = (int)lightSource.RampMode;
            vertex.LightProperties.W = (lightSource.CastsShadows && (DistanceField != null)) ? 1f : 0f;
            vertex.MoreLightProperties.X = lightSource.AmbientOcclusionRadius;
            vertex.MoreLightProperties.Y = lightSource.ShadowDistanceFalloff.GetValueOrDefault(-99999);
            vertex.MoreLightProperties.Z = lightSource.FalloffYFactor;
            vertex.MoreLightProperties.W = lightSource.AmbientOcclusionOpacity;
            vertex.EvenMoreLightProperties = Vector4.Zero;
            ltrs.LightVertices.Add(in vertex);

            ltrs.LightCount++;
        }

        private void RenderDirectionalLightSource (DirectionalLightSource lightSource, float intensityScale, LightTypeRenderState ltrs) {
            LightVertex vertex;
            if (lightSource.Bounds.HasValue) {
                // FIXME: 3D bounds?
                vertex.LightPosition1 = new Vector4(lightSource.Bounds.Value.TopLeft, 0, 0);
                vertex.LightPosition2 = new Vector4(lightSource.Bounds.Value.BottomRight, 0, 0);
            } else {
                vertex.LightPosition1 = new Vector4(-99999, -99999, 0, 0);
                vertex.LightPosition2 = new Vector4(99999, 99999, 0, 0);
            }
            vertex.LightPosition3 = Vector4.Zero;
            var color = lightSource.Color;
            color.W *= (lightSource.Opacity * intensityScale);
            vertex.Color1 = color;
            if (lightSource._Direction.HasValue)
                vertex.Color2 = new Vector4(lightSource._Direction.Value, 1.0f);
            else
                vertex.Color2 = Vector4.Zero;
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
            vertex.EvenMoreLightProperties = Vector4.Zero;

            var lightBounds = new Bounds(
                Vector2.Zero,
                // HACK to ensure that only one of the two triangles passes the clip test
                //  so we don't end up with wasted rasterization work along the seam
                new Vector2(
                    Configuration.MaximumRenderSize.First * 2,
                    Configuration.MaximumRenderSize.Second * 2
                )
            );

            ltrs.LightVertices.Add(in vertex);
            ltrs.LightCount++;
        }

        private void RenderLineLightSource (LineLightSource lightSource, float intensityScale, LightTypeRenderState ltrs) {
            LightVertex vertex;
            vertex.LightPosition1 = new Vector4(lightSource.StartPosition, 0);
            vertex.LightPosition2 = new Vector4(lightSource.EndPosition, 0);
            vertex.LightPosition3 = Vector4.Zero;
            Vector4 color1 = lightSource.StartColor, color2 = lightSource.EndColor;
            color1.W *= (lightSource.Opacity * intensityScale);
            color2.W *= (lightSource.Opacity * intensityScale);
            vertex.Color1 = color1;
            vertex.Color2 = color2;
            vertex.LightProperties.X = lightSource.Radius;
            vertex.LightProperties.Y = 0;
            vertex.LightProperties.Z = (int)lightSource.RampMode;
            vertex.LightProperties.W = (lightSource.CastsShadows && (DistanceField != null)) ? 1f : 0f;
            vertex.MoreLightProperties.X = lightSource.AmbientOcclusionRadius;
            vertex.MoreLightProperties.Y = lightSource.ShadowDistanceFalloff.GetValueOrDefault(-99999);
            vertex.MoreLightProperties.Z = lightSource.FalloffYFactor;
            vertex.MoreLightProperties.W = lightSource.AmbientOcclusionOpacity;
            vertex.EvenMoreLightProperties = Vector4.Zero;
            ltrs.LightVertices.Add(in vertex);

            ltrs.LightCount++;
        }

        private void RenderProjectorLightSource (ProjectorLightSource lightSource, float intensityScale, LightTypeRenderState ltrs) {
            LightVertex vertex;
            Matrix m = lightSource.Transform, invM;
            var tex = lightSource.TextureRef.Instance;
            if (tex == null)
                return;

            var texSize = new Vector2(tex.Width, tex.Height);

            m *= Matrix.CreateScale(texSize.X * lightSource.Scale.X, texSize.Y * lightSource.Scale.Y, lightSource.Depth.GetValueOrDefault(Environment.MaximumZ));
            m *= Matrix.CreateTranslation(lightSource.Position);

            Matrix.Invert(ref m, out invM);

            if (lightSource.Rotation != Quaternion.Identity) {
                // Once the screen coordinates have been converted into texture space, 
                //  rotate around the center of the texture

                // Compute an aspect ratio factor and apply it before performing the rotation so that
                //  the aspect ratio and size of the texture are preserved
                // FIXME: This isn't necessary or helpful anymore?
                // var rgnSize = texSize * lightSource.TextureRegion.Size;
                // var aspect = rgnSize.Y / (float)rgnSize.X;

                invM *= Matrix.CreateTranslation(new Vector3(-lightSource.TextureRegion.Size * 0.5f, 0));
                // invM *= Matrix.CreateScale(aspect, 1, 1);
                invM *= Matrix.CreateFromQuaternion(lightSource.Rotation);
                // invM *= Matrix.CreateScale(1.0f / aspect, 1, 1);
                invM *= Matrix.CreateTranslation(new Vector3(lightSource.TextureRegion.Size * 0.5f, 0));
            }

            var effectiveScale2 = lightSource.Scale * Configuration.RenderScale;
            var approximateScale = (effectiveScale2.X + effectiveScale2.Y) / 2.0;
            var invApproximateScale = 1.0 / approximateScale;
            var mipBias = (float)Math.Max(0, Math.Log(invApproximateScale, 2) + Configuration.ProjectorMipBias);

            vertex.LightPosition1 = new Vector4(invM.M11, invM.M12, invM.M13, invM.M14);
            vertex.LightPosition2 = new Vector4(invM.M21, invM.M22, invM.M23, invM.M24);
            vertex.LightPosition3 = lightSource.Origin.HasValue
                ? new Vector4(lightSource.Origin.Value, 1)
                : Vector4.Zero;
            vertex.Color1         = new Vector4(invM.M31, invM.M32, invM.M33, invM.M34);
            vertex.Color2         = new Vector4(invM.M41, invM.M42, invM.M43, mipBias);
            vertex.LightProperties.X = lightSource.Radius;
            vertex.LightProperties.Y = lightSource.RampLength;
            vertex.LightProperties.Z = (int)lightSource.RampMode;
            vertex.LightProperties.W = (lightSource.CastsShadows && (DistanceField != null) && lightSource.Origin.HasValue) ? 1f : 0f;
            vertex.MoreLightProperties.X = lightSource.AmbientOcclusionRadius;
            vertex.MoreLightProperties.Y = lightSource.Opacity * intensityScale;
            vertex.MoreLightProperties.Z = lightSource.Wrap ? 0 : 1;
            vertex.MoreLightProperties.W = lightSource.AmbientOcclusionOpacity;
            vertex.EvenMoreLightProperties = new Vector4(
                lightSource.TextureRegion.TopLeft.X,
                lightSource.TextureRegion.TopLeft.Y,
                lightSource.TextureRegion.BottomRight.X,
                lightSource.TextureRegion.BottomRight.Y
            );
            ltrs.LightVertices.Add(in vertex);

            ltrs.LightCount++;
        }

        private class LightingResolveHandler {
            public readonly LightingRenderer Renderer;
            public Material m;
            public HDRConfiguration? hdr;
            public Vector2 uvOffset;
            public bool usedLut;
            public LUTBlendingConfiguration? lutBlending;

            public readonly Action<DeviceManager, object> Before, After;

            public LightingResolveHandler (LightingRenderer renderer) {
                Renderer = renderer;
                Before = _Before;
                After = _After;
            }

            private void _Before (DeviceManager dm, object _) {
                // FIXME: RenderScale?
                var p = m.Effect.Parameters;

                Renderer.SetGBufferParameters(p);
                p["InverseScaleFactor"].SetValue(
                    hdr.HasValue
                        ? ((hdr.Value.InverseScaleFactor != 0) ? hdr.Value.InverseScaleFactor : 1.0f)
                        : 1.0f
                );
                p["AlbedoIsSRGB"].SetValue(
                    hdr.HasValue
                        ? (hdr.Value.AlbedoIsSRGB ? 1f : 0f)
                        : 0f
                );
                p["ResolveToSRGB"].SetValue(
                    hdr.HasValue
                        ? (hdr.Value.ResolveToSRGB ? 1f : 0f)
                        : 0f
                );
                m.Parameters.LightmapUVOffset.SetValue(uvOffset);

                if (usedLut)
                    IlluminantMaterials.SetLUTBlending(m, lutBlending.Value);

                var ds = (hdr.HasValue && hdr.Value.Dithering.HasValue)
                    ? hdr.Value.Dithering.Value
                    : new DitheringSettings {
                        Unit = 255f,
                        Strength = 0f
                    };
                ds.FrameIndex = dm.FrameIndex;

                Renderer.uDithering.Set(m, in ds);
                Renderer.EnvironmentUniforms.SetIntoParameters(p);

                if (hdr.HasValue) {
                    if (hdr.Value.Mode == HDRMode.GammaCompress)
                        Renderer.IlluminantMaterials.SetGammaCompressionParameters(
                            hdr.Value.GammaCompression.MiddleGray,
                            hdr.Value.GammaCompression.AverageLuminance,
                            hdr.Value.GammaCompression.MaximumLuminance,
                            hdr.Value.Offset
                        );
                    else if (hdr.Value.Mode == HDRMode.ToneMap)
                        Renderer.IlluminantMaterials.SetToneMappingParameters(
                            hdr.Value.Exposure,
                            hdr.Value.ToneMapping.WhitePoint,
                            hdr.Value.Offset,
                            hdr.Value.Gamma
                        );
                    else 
                        Renderer.IlluminantMaterials.SetToneMappingParameters(
                            hdr.Value.Exposure,
                            1f,
                            hdr.Value.Offset,
                            hdr.Value.Gamma
                        );
                }
            }

            private void _After (DeviceManager dm, object _) {
                Interlocked.CompareExchange(ref Renderer._AvailableResolveHandler, this, null);
            }
        }

        private LightingResolveHandler _AvailableResolveHandler = null;

        /// <summary>
        /// Resolves the current lightmap into the specified batch container on the specified layer.
        /// </summary>
        /// <param name="container">The batch container to resolve lighting into.</param>
        /// <param name="layer">The layer to resolve lighting into.</param>
        private void ResolveLighting (
            IBatchContainer container, int layer,
            RenderTarget2D lightmap,
            Vector2 position, Vector2 scale, 
            Texture2D albedo, Bounds? albedoRegion, SamplerState albedoSamplerState, SamplerState lightmapSamplerState,
            Vector2 uvOffset, HDRConfiguration? hdr, LUTBlendingConfiguration? lutBlending,
            BlendState blendState, bool worldSpace
        ) {
            Material m;
            bool usedLut = false;
            var gc = hdr.HasValue && hdr.Value.Mode == HDRMode.GammaCompress;
            var tm = hdr.HasValue && hdr.Value.Mode == HDRMode.ToneMap;
            if (worldSpace) {
                if (albedo != null) {
                    if (gc)
                        m = IlluminantMaterials.WorldSpaceGammaCompressedLightingResolveWithAlbedo;
                    else if (tm)
                        m = IlluminantMaterials.WorldSpaceToneMappedLightingResolveWithAlbedo;
                    else if (lutBlending.HasValue) {
                        usedLut = true;
                        m = IlluminantMaterials.WorldSpaceLUTBlendedLightingResolveWithAlbedo;
                    } else
                        m = IlluminantMaterials.WorldSpaceLightingResolveWithAlbedo;
                } else {
                    if (gc)
                        m = IlluminantMaterials.WorldSpaceGammaCompressedLightingResolve;
                    else if (tm)
                        m = IlluminantMaterials.WorldSpaceToneMappedLightingResolve;
                    else
                        m = IlluminantMaterials.WorldSpaceLightingResolve;
                }
            } else {
                if (albedo != null) {
                    if (gc)
                        m = IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolveWithAlbedo;
                    else if (tm)
                        m = IlluminantMaterials.ScreenSpaceToneMappedLightingResolveWithAlbedo;
                    else if (lutBlending.HasValue) {
                        usedLut = true;
                        m = IlluminantMaterials.ScreenSpaceLUTBlendedLightingResolveWithAlbedo;
                    } else
                        m = IlluminantMaterials.ScreenSpaceLightingResolveWithAlbedo;
                } else {
                    if (gc)
                        m = IlluminantMaterials.ScreenSpaceGammaCompressedLightingResolve;
                    else if (tm)
                        m = IlluminantMaterials.ScreenSpaceToneMappedLightingResolve;
                    else
                        m = IlluminantMaterials.ScreenSpaceLightingResolve;
                }
            }

            if (lutBlending.HasValue && !usedLut && (albedo != null))
                throw new ArgumentException("LUT blending is not compatible with this type of lighting resolve.");

            if (blendState != null)
                m = Materials.Get(m, blendState: blendState);

            int w, h;
            Configuration.GetRenderSize(out w, out h);
            
            var lightmapBounds = lightmap.BoundsFromRectangle(
                new Rectangle(0, 0, w, h)
            );
            var albedoBounds = albedoRegion.GetValueOrDefault(Bounds.Unit);

            LightingResolveHandler resolveHandler;
            resolveHandler = Interlocked.Exchange(ref _AvailableResolveHandler, null);
            if (resolveHandler == null)
                resolveHandler = new LightingResolveHandler(this);

            resolveHandler.hdr = hdr;
            resolveHandler.lutBlending = lutBlending;
            resolveHandler.m = m;
            resolveHandler.usedLut = usedLut;
            resolveHandler.uvOffset = uvOffset;

            // HACK: This is a little gross
            using (var group = BatchGroup.New(
                container, layer, before: resolveHandler.Before, after: resolveHandler.After
            )) {
                using (var bb = BitmapBatch.New(
                    group, 0, m, 
                    samplerState: albedoSamplerState ?? SamplerState.LinearClamp,
                    samplerState2: lightmapSamplerState ?? SamplerState.LinearClamp
                )) {
                    BitmapDrawCall dc;
                    if (albedo != null) {
                        dc = new BitmapDrawCall(
                            albedo, position, albedoBounds
                        ) {
                            TextureRegion2 = lightmapBounds,
                            Textures = new TextureSet(albedo, lightmap),
                            Scale = scale
                        };
                    } else {
                        dc = new BitmapDrawCall(
                            lightmap, position, lightmapBounds
                        ) {
                            Scale = scale / Configuration.RenderScale
                        };
                    }

                    bb.Add(in dc);
                }
            }
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
            Vector3? lightDirection = null,
            Bounds3? worldBounds = null,
            float outlineSize = 1.8f
        ) {
            if ((_DistanceField == null) && (singleObject == null))
                return new VisualizationInfo { Failed = true };

            ComputeUniforms();

            viewDirection.Normalize();

            var tl = new Vector3(rectangle.TopLeft, 0);
            var tr = new Vector3(rectangle.TopRight, 0);
            var bl = new Vector3(rectangle.BottomLeft, 0);
            var br = new Vector3(rectangle.BottomRight, 0);

            Vector3 worldMin, worldMax;
            if (worldBounds != null) {
                worldMin = worldBounds.Value.Minimum;
                worldMax = worldBounds.Value.Maximum;
            } else {
                if (_DistanceField != null) {
                    worldMin = Vector3.Zero;
                    worldMax = new Vector3(
                        _DistanceField.VirtualWidth,
                        _DistanceField.VirtualHeight,
                        Environment.MaximumZ
                    );
                } else {
                    var sz = singleObject.Bounds3;
                    worldMin = sz.Minimum;
                    worldMax = sz.Maximum;
                }
            }
            var extent = worldMax - worldMin;
            var center = (worldMin + worldMax) / 2f;
            var centerMask = new Vector3(
                Math.Abs(Math.Sign(viewDirection.X)),
                Math.Abs(Math.Sign(viewDirection.Y)),
                Math.Abs(Math.Sign(viewDirection.Z))
            );
            var halfDisplaySize = new Vector3(
                MathHelper.Lerp(extent.X / 2f, 0, centerMask.X),
                MathHelper.Lerp(extent.Y / 2f, 0, centerMask.Y),
                MathHelper.Lerp(extent.Z / 2f, 0, centerMask.Z)
            );
            center = new Vector3(
                MathHelper.Lerp(center.X, worldMin.X, centerMask.X),
                MathHelper.Lerp(center.Y, worldMin.Y, centerMask.Y),
                MathHelper.Lerp(center.Z, worldMin.Z, centerMask.Z)
            );
            var halfTexel = new Vector3(-0.5f * (1.0f / rectangle.Size.X), -0.5f * (1.0f / rectangle.Size.Y), 0);

            // HACK: Pick an appropriate length that will always travel through the whole field
            var rayLength = extent.Length() * 2f;
            var rayVector = viewDirection * rayLength;
            Vector3 rayOrigin;

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

            // HACK: Place our view plane somewhere reasonable
            {
                var ray = new Ray(center, -viewDirection);
                var planeCenter = FindBoxIntersection(ray, worldMin, worldMax);
                if (!planeCenter.HasValue)
                    return new VisualizationInfo { Failed = true, Right = right, Up = up, ViewDirection = viewDirection };
                rayOrigin = planeCenter.Value - viewDirection;
            }

            Vector3 worldTL, worldTR, worldBL, worldBR;

            var absViewDirection = new Vector3(
                Math.Abs(viewDirection.X),
                Math.Abs(viewDirection.Y),
                Math.Abs(viewDirection.Z)
            );
            var planeRight = Vector3.Cross(absViewDirection, up);
            var planeUp = Vector3.Cross(absViewDirection, right);

            worldTL = rayOrigin + (-planeRight * halfDisplaySize) + (-planeUp * halfDisplaySize);
            worldTR = rayOrigin + ( planeRight * halfDisplaySize) + (-planeUp * halfDisplaySize);
            worldBL = rayOrigin + (-planeRight * halfDisplaySize) + ( planeUp * halfDisplaySize);
            worldBR = rayOrigin + ( planeRight * halfDisplaySize) + ( planeUp * halfDisplaySize);

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
                material = mode != VisualizationMode.Surfaces
                    ? IlluminantMaterials.FunctionOutline
                    : IlluminantMaterials.FunctionSurface;
            } else {
                material = mode != VisualizationMode.Surfaces
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
                // FIXME: Create reusable delegate instance + use userData
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
                        p["FunctionType"].SetValue((int)singleObject.Type + 1);
                        p["FunctionCenter"].SetValue(singleObject.Center);
                        p["FunctionSize"].SetValue(singleObject.Size);
                        p["FunctionRotation"].SetValue(singleObject.Rotation);
                    }

                    p["OutlineSize"]?.SetValue(Math.Max(outlineSize, 1f));
                    p["FilledInterior"]?.SetValue((mode == VisualizationMode.Silhouettes) ? 1f : 0f);
                }
            )) {
                batch.Add(new PrimitiveDrawCall<VisualizeDistanceFieldVertex>(
                    PrimitiveType.TriangleList, verts, 0, 4, QuadIndices, 0, 2
                ));
            }

            return new VisualizationInfo {
                ViewCenter = rayOrigin,
                Up = planeUp,
                Right = planeRight,
                ViewDirection = viewDirection
            };
        }

        private void SetDistanceFieldParameters (
            Material m, bool setDistanceTexture,
            RendererQualitySettings q
        ) {
            Uniforms.DistanceField dfu;
            var p = m.Effect.Parameters;

            EnvironmentUniforms.SetIntoParameters(p);

            if (_DistanceField == null) {
                dfu = new Uniforms.DistanceField();
                dfu.InvScaleFactorX = dfu.InvScaleFactorY = 1;
                dfu.Extent.Z = Environment.MaximumZ;
                uDistanceField.TrySet(m, in dfu);
                p["DistanceFieldPacked1"]?.SetValue(Vector4.Zero);
                p.ClearTexture("DistanceFieldTexture");
#if DF3D
                p.ClearTexture("DistanceFieldTexture3D");
#endif
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

            uDistanceField.TrySet(m, in dfu);

            if (setDistanceTexture) {
                p["DistanceFieldTexture"]?.SetValue(_DistanceField.Texture.Get());
#if DF3D
                p["DistanceFieldTexture3D"]?.SetValue(_DistanceField.Texture3D);
#endif
            }

            p["DistanceFieldPacked1"]?.SetValue(new Vector4(
                // FIXME: Surprisingly, using double precision for 1/3 here breaks
                //  the top of the ParticleLights wall and makes it black...
                (float)((1.0f / Math.Max(0.0001f, dfu.TextureSliceCount.X)) * (1.0f / 3.0f)), 
                (float)((1.0f / Math.Max(0.0001f, dfu.Extent.Z)) * dfu.TextureSliceCount.W),
                dfu.TextureSliceCount.Z, dfu.MinimumLength
            ));
        }

        public void InvalidateFields (
            bool invalidateDistanceField = true
        ) {
            if (invalidateDistanceField)
                _DistanceField?.Invalidate();
        }

        public void UpdateFields (
            IBatchContainer container, int layer, 
            Vector2? viewportPosition = null, Vector2? viewportScale = null
        ) {
            EnsureGBuffer();

            ComputeUniforms();

            var viewportChanged = (PendingFieldViewportPosition != viewportPosition) || (PendingFieldViewportScale != viewportScale);
            var paramsChanged = (Environment.ZToYMultiplier != PreviousZToYMultiplier);

            PendingFieldViewportPosition = viewportPosition;
            PendingFieldViewportScale = viewportScale;
            PreviousZToYMultiplier = Environment.ZToYMultiplier;

            if (_GBuffer != null) {
                var renderWidth = (int)(Configuration.MaximumRenderSize.First * Configuration.RenderScale.X);
                var renderHeight = (int)(Configuration.MaximumRenderSize.Second * Configuration.RenderScale.Y);

                RenderGBuffer(ref layer, container, renderWidth, renderHeight);
            }

            if (_DistanceField != null) {
                AutoInvalidateDistanceField();

                if (_DistanceField.NeedsRasterize)
                    RenderDistanceField(ref layer, container);
            }
        }

        private void AutoInvalidateDistanceField () {
            var ddf = (_DistanceField as DynamicDistanceField);
            bool hasInvalidatedStatic = false, hasInvalidatedDynamic = false;

            if (Environment.Obstructions.IsInvalidDynamic) {
                ddf?.Invalidate(false);
                hasInvalidatedDynamic = true;
            }

            if (Environment.Obstructions.IsInvalid) {
                _DistanceField.Invalidate();
                hasInvalidatedStatic = true;
            }

            Environment.Obstructions.IsInvalid = Environment.Obstructions.IsInvalidDynamic = false;

            foreach (var obs in Environment.Obstructions) {
                if (obs.HasDynamicityChanged) {
                    obs.HasDynamicityChanged = false;
                    if (!hasInvalidatedStatic) {
                        hasInvalidatedStatic = hasInvalidatedDynamic = true;
                        _DistanceField.Invalidate();
                    }
                }

                if (!obs.IsValid) {
                    obs.IsValid = true;
                    if ((ddf != null) && obs.IsDynamic) {
                        if (!hasInvalidatedDynamic) {
                            hasInvalidatedDynamic = true;
                            ddf.Invalidate(false);
                        }
                    } else if (!hasInvalidatedStatic) {
                        hasInvalidatedStatic = hasInvalidatedDynamic = true;
                        _DistanceField.Invalidate();
                    }
                }
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
        Outlines,
        Silhouettes
    }

    public struct VisualizationInfo {
        public bool Failed;
        public Vector3 ViewCenter, ViewDirection;
        public Vector3 Up, Right;
    }

    public sealed class LightSorter : IRefComparer<LightSource> {
        public static readonly LightSorter Instance = new LightSorter();

        public int Compare (ref LightSource x, ref LightSource y) {
            int result = x.SortKey - y.SortKey;
            if (result != 0)
                return result;

            int xBlendID = 0, yBlendID = 0;
            if (x.BlendMode != null)
                xBlendID = x.BlendMode.GetHashCode();
            if (y.BlendMode != null)
                yBlendID = y.BlendMode.GetHashCode();

            int xTexID = 0, yTexID = 0;
            if (x.TextureRef != null)
                xTexID = x.TextureRef.GetHashCode();
            if (y.TextureRef != null)
                yTexID = y.TextureRef.GetHashCode();

            result = xBlendID - yBlendID;
            if (result == 0)
                result = xTexID - yTexID;
            if (result == 0)
                result = x.TypeID - y.TypeID;
            return result;
        }
    }
}
