// #define SHADOW_VIZ

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
        public float ClipRegionScale         = 1.0f;
        public bool  TwoPointFiveD           = false;
        public float ZOffset                 = 0.0f;
        public float ZScale                  = 1.0f;
        public float HeightmapResolution     = 1.0f;
        public float DistanceFieldStepSize   = 3.0f;
        public float DistanceFieldResolution = 1.0f;
        public int   DistanceFieldSliceCount = 1;
        public float DistanceFieldOcclusionToOpacityPower = 1;
        public float DistanceFieldMaxConeRadius = 8;
        public float DistanceFieldConeGrowthRate = 1;

        public readonly Pair<int> MaximumRenderSize;

        public RendererConfiguration (int maxWidth, int maxHeight) {
            MaximumRenderSize = new Pair<int>(maxWidth, maxHeight);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FrontFaceVertex : IVertexType {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 ZRange;

        public static VertexDeclaration _VertexDeclaration;

        static FrontFaceVertex () {
            var tThis = typeof(FrontFaceVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "Normal").ToInt32(),   VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "ZRange").ToInt32(),   VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
            );
        }

        public FrontFaceVertex (Vector3 position, Vector3 normal, Vector2 zRange) {
            Position = position;
            Normal = normal;
            ZRange = zRange;
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    public class LightingRenderer : IDisposable {
        private class ArrayLineWriter : ILineWriter {
            private int _Count = 0;
            private ShadowVertex[] VertexBuffer;
            private short[] IndexBuffer;

            public void Write (
                Vector2 a, Vector2 aHeights,
                Vector2 b, Vector2 bHeights
            ) {
                ShadowVertex vertex;
                int vertexOffset = _Count * 4;
                int indexOffset = _Count * 6;

                vertex.Position = new Vector3(a, aHeights.Y);
                vertex.MinZ = aHeights.X;

                vertex.PairIndex = 0;
                VertexBuffer[vertexOffset + 0] = vertex;
                vertex.PairIndex = 1;
                VertexBuffer[vertexOffset + 1] = vertex;

                vertex.Position = new Vector3(b, bHeights.Y);
                vertex.MinZ = bHeights.X;

                vertex.PairIndex = 0;
                VertexBuffer[vertexOffset + 2] = vertex;
                vertex.PairIndex = 1;
                VertexBuffer[vertexOffset + 3] = vertex;

                for (var j = 0; j < ShadowIndices.Length; j++)
                    IndexBuffer[indexOffset + j] = (short)(vertexOffset + ShadowIndices[j]);

                _Count += 1;
            }

            public int LinesWritten {
                get {
                    return _Count;
                }
            }

            internal int Finish () {
                int result = _Count;

                _Count = -1;
                VertexBuffer = null;
                IndexBuffer = null;

                return result;
            }

            internal void SetOutput (ShadowVertex[] vertexBuffer, short[] indexBuffer) {
                _Count = 0;
                VertexBuffer = vertexBuffer;
                IndexBuffer = indexBuffer;
            }
        }

        private class VisualizerLineWriter : ILineWriter {
            public GeometryBatch Batch;
            public Color Color;

            public void Write (
                Vector2 a, Vector2 aHeights,
                Vector2 b, Vector2 bHeights
            ) {
                Batch.AddLine(a, b, Color);
            }
        }

        public class CachedSector : IDisposable {
            public Pair<int> SectorIndex;
            public int FrameIndex;
            public int DrawnIndex;
            public DynamicVertexBuffer ObstructionVertexBuffer;
            public DynamicIndexBuffer ObstructionIndexBuffer;
            public int VertexCount, IndexCount, PrimitiveCount;

            public void Dispose () {
                if (ObstructionVertexBuffer != null)
                    ObstructionVertexBuffer.Dispose();
                if (ObstructionIndexBuffer != null)
                    ObstructionIndexBuffer.Dispose();
            }
        }

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
        public readonly BlendState        HeightMin, HeightMax, OddSlice, EvenSlice;

        private static readonly short[] ShadowIndices;
        private static readonly Dictionary<TextureFilter, SamplerState> RampSamplerStates = new Dictionary<TextureFilter, SamplerState>();

        private readonly ArrayLineWriter ArrayLineWriterInstance = new ArrayLineWriter();
        private readonly VisualizerLineWriter VisualizerLineWriterInstance = new VisualizerLineWriter();
        private readonly LightSourceComparer LightSourceComparerInstance = new LightSourceComparer();

        private PointLightVertex[] PointLightVertices = new PointLightVertex[128];
        private short[] PointLightIndices = null;
        private readonly Dictionary<Pair<int>, CachedSector> SectorCache = new Dictionary<Pair<int>, CachedSector>(new IntPairComparer());
        private readonly List<PointLightRecord> PointLightBatchBuffer = new List<PointLightRecord>(128);
        private Rectangle StoredScissorRect;

        private readonly RenderTarget2D _TerrainDepthmap;
        private readonly RenderTarget2D _DistanceField;

        private readonly Action<DeviceManager, object> BeginLightPass, EndLightPass, RestoreScissorRect, IlluminationBatchSetup;

        public readonly RendererConfiguration Configuration;
        public LightingEnvironment Environment;

        const int   DistanceLimit = 258;
        const int   StencilTrue  = 0xFF;
        const int   StencilFalse = 0x00;

        public readonly int DistanceFieldSliceWidth, DistanceFieldSliceHeight;
        public readonly int DistanceFieldSlicesX, DistanceFieldSlicesY;

        public readonly bool  HighPrecisionTerrain = true;

        static LightingRenderer () {
            ShadowIndices = new short[] {
                0, 1, 2,
                1, 2, 3
            };
        }

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
            RestoreScissorRect = _RestoreScissorRect;
            IlluminationBatchSetup = _IlluminationBatchSetup;

            lock (coordinator.CreateResourceLock) {
                // Can't use HalfVector2 because lol, xna =[
                SurfaceFormat fmt =
                    HighPrecisionTerrain
                        ? SurfaceFormat.Rg32
                        : SurfaceFormat.Color;

                _TerrainDepthmap = new RenderTarget2D(
                    coordinator.Device, 
                    (int)(Configuration.MaximumRenderSize.First * Configuration.HeightmapResolution), 
                    (int)(Configuration.MaximumRenderSize.Second * Configuration.HeightmapResolution),
                    false, fmt, DepthFormat.None, 0, RenderTargetUsage.DiscardContents
                );

                DistanceFieldSliceWidth = (int)(Configuration.MaximumRenderSize.First * Configuration.DistanceFieldResolution);
                DistanceFieldSliceHeight = (int)(Configuration.MaximumRenderSize.Second * Configuration.DistanceFieldResolution);
                int maxSlicesX = 4096 / DistanceFieldSliceWidth;
                int maxSlicesY = 4096 / DistanceFieldSliceHeight;
                // HACK: We encode odd/even slices in the red and green channels
                int maxSlices = maxSlicesX * maxSlicesY * 2;

                // FIXME: Should we abort? The user can't easily determine a good slice count given a resolution
                if (Configuration.DistanceFieldSliceCount > maxSlices) {
                    Configuration.DistanceFieldSliceCount = maxSlices;
                    //throw new ArgumentOutOfRangeException("Too many distance field slices requested for this size. Maximum is " + maxSlices);
                }

                int effectiveSliceCount = (Configuration.DistanceFieldSliceCount + 1) / 2;

                DistanceFieldSlicesX = Math.Min(maxSlicesX, effectiveSliceCount);
                DistanceFieldSlicesY = Math.Max((int)Math.Ceiling(effectiveSliceCount / (float)maxSlicesX), 1);

                _DistanceField = new RenderTarget2D(
                    coordinator.Device,
                    DistanceFieldSliceWidth * DistanceFieldSlicesX, 
                    DistanceFieldSliceHeight * DistanceFieldSlicesY,
                    false, SurfaceFormat.Rg32, DepthFormat.None, 0, RenderTargetUsage.DiscardContents
                );
            }

            IlluminantMaterials.ClearStencil = materials.Get(
                materials.Clear, 
                rasterizerState: RasterizerState.CullNone, 
                depthStencilState: new DepthStencilState {
                    StencilEnable = true,
                    StencilMask = StencilTrue,
                    StencilWriteMask = StencilTrue,
                    ReferenceStencil = StencilFalse,
                    StencilFunction = CompareFunction.Always,
                    StencilPass = StencilOperation.Replace,
                    StencilFail = StencilOperation.Replace,
                },
                blendState: BlendState.Opaque
            );

            materials.Add(
                IlluminantMaterials.DebugOutlines = materials.WorldSpaceGeometry.SetStates(
                    blendState: BlendState.AlphaBlend
                )
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

            HeightMin = new BlendState {
                ColorWriteChannels    = ColorWriteChannels.Red,
                ColorBlendFunction    = BlendFunction.Min,
                ColorSourceBlend      = Blend.One,
                ColorDestinationBlend = Blend.One
            };

            HeightMax = new BlendState {
                ColorWriteChannels    = ColorWriteChannels.Green,
                ColorBlendFunction    = BlendFunction.Max,
                ColorSourceBlend      = Blend.One,
                ColorDestinationBlend = Blend.One
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

            {
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RenderStates.ScissorOnly, 
                        depthStencilState: DepthStencilState.None
                    )
                };
                var dEnd = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone, 
                        depthStencilState: DepthStencilState.None
                    )
                };

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

                materials.Add(IlluminantMaterials.VisualizeDistanceField = 
                    new Squared.Render.EffectMaterial(content.Load<Effect>("Visualize"), "Visualize"));
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

            // Reduce garbage created by BufferPool<>.Allocate when creating cached sectors
            BufferPool<ShadowVertex>.MaxBufferSize = 1024 * 16;
            BufferPool<short>.MaxBufferSize = 1024 * 32;
        }

        public void Dispose () {
            foreach (var cachedSector in SectorCache.Values)
                cachedSector.Dispose();

            SectorCache.Clear();
        }

        public RenderTarget2D TerrainDepthmap {
            get {
                return _TerrainDepthmap;
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
            StoredScissorRect = device.Device.ScissorRectangle;
        }

        private void _RestoreScissorRect (DeviceManager device, object userData) {
            device.Device.ScissorRectangle = StoredScissorRect;
        }

        private void _EndLightPass (DeviceManager device, object userData) {
            device.Device.ScissorRectangle = StoredScissorRect;
            device.PopStates();
        }

        private Rectangle GetScissorRectForLightSource (DeviceManager device, LightSource ls) {
            Bounds scissorBounds;

            // FIXME: Replace this with a use of the material set's modelview/projection matrix and device
            //  viewport to 'project' the clip region to scissor coordinates?
            var scale = new Vector2(
                Materials.ViewportScale.X * Configuration.ClipRegionScale,
                Materials.ViewportScale.Y * Configuration.ClipRegionScale
            );

            if (ls.ClipRegion.HasValue) {
                scissorBounds = new Bounds(
                    (ls.ClipRegion.Value.TopLeft - Materials.ViewportPosition) * scale,
                    (ls.ClipRegion.Value.BottomRight - Materials.ViewportPosition) * scale
                );
            } else {
                scissorBounds = new Bounds(
                    ((Vector2)ls.Position - new Vector2(ls.RampEnd) - Materials.ViewportPosition) * scale,
                    ((Vector2)ls.Position + new Vector2(ls.RampEnd) - Materials.ViewportPosition) * scale
                );
            }

            var scissor = new Rectangle(
                (int)Math.Floor(scissorBounds.TopLeft.X),
                (int)Math.Floor(scissorBounds.TopLeft.Y),
                (int)Math.Ceiling(scissorBounds.Size.X),
                (int)Math.Ceiling(scissorBounds.Size.Y)
            );

            var result = Rectangle.Intersect(scissor, StoredScissorRect);
            return result;
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
                    1f / Configuration.MaximumRenderSize.First, 
                    1f / Configuration.MaximumRenderSize.Second
                );
                mi.Effect.Parameters["TerrainTextureTexelSize"].SetValue(tsize);
                mi.Effect.Parameters["TerrainTexture"].SetValue(_TerrainDepthmap);

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
            device.Device.ScissorRectangle = StoredScissorRect;
        }

        private CachedSector GetCachedSector (Frame frame, Pair<int> sectorIndex) {
            CachedSector result;
            if (!SectorCache.TryGetValue(sectorIndex, out result))
                SectorCache.Add(sectorIndex, result = new CachedSector {
                    SectorIndex = sectorIndex
                });

            if (result.FrameIndex == frame.Index)
                return result;

            SpatialCollection<HeightVolumeBase>.Sector heightSector;
            SpatialCollection<LightObstructionBase>.Sector obsSector;

            Environment.HeightVolumes.TryGetSector(sectorIndex, out heightSector);
            Environment.Obstructions.TryGetSector(sectorIndex, out obsSector);

            int lineCount = 0;

            if (heightSector != null)
            foreach (var item in heightSector)
                lineCount += item.Item.LineCount;

            if (obsSector != null)
            foreach (var item in obsSector)
                lineCount += item.Item.LineCount;

            result.PrimitiveCount = lineCount * 2;
            result.VertexCount = lineCount * 4;
            result.IndexCount = lineCount * 6;

            if ((result.ObstructionVertexBuffer != null) && (result.ObstructionVertexBuffer.VertexCount < result.VertexCount)) {
                lock (Coordinator.CreateResourceLock)
                    result.ObstructionVertexBuffer.Dispose();
                
                result.ObstructionVertexBuffer = null;
            }

            if ((result.ObstructionIndexBuffer != null) && (result.ObstructionIndexBuffer.IndexCount < result.IndexCount)) {
                lock (Coordinator.CreateResourceLock)
                    result.ObstructionIndexBuffer.Dispose();
                
                result.ObstructionIndexBuffer = null;
            }

            if (result.ObstructionVertexBuffer == null) {
                lock (Coordinator.CreateResourceLock)
                    result.ObstructionVertexBuffer = new DynamicVertexBuffer(frame.RenderManager.DeviceManager.Device, (new ShadowVertex().VertexDeclaration), result.VertexCount, BufferUsage.WriteOnly);
            }

            if (result.ObstructionIndexBuffer == null) {
                lock (Coordinator.CreateResourceLock)
                    result.ObstructionIndexBuffer = new DynamicIndexBuffer(frame.RenderManager.DeviceManager.Device, IndexElementSize.SixteenBits, result.IndexCount, BufferUsage.WriteOnly);
            }

            using (var va = BufferPool<ShadowVertex>.Allocate(result.VertexCount))
            using (var ia = BufferPool<short>.Allocate(result.IndexCount)) {
                var vb = va.Data;
                var ib = ia.Data;

                ArrayLineWriterInstance.SetOutput(vb, ib);

                if (heightSector != null)
                foreach (var itemInfo in heightSector)
                    itemInfo.Item.GenerateLines(ArrayLineWriterInstance);

                if (obsSector != null)
                foreach (var itemInfo in obsSector)
                    itemInfo.Item.GenerateLines(ArrayLineWriterInstance);

                var linesWritten = ArrayLineWriterInstance.Finish();
                if (linesWritten != lineCount)
                    throw new InvalidDataException("GenerateLines didn't generate enough lines based on LineCount");

                lock (Coordinator.UseResourceLock) {
                    result.ObstructionVertexBuffer.SetData(vb, 0, result.VertexCount, SetDataOptions.Discard);
                    result.ObstructionIndexBuffer.SetData(ib, 0, result.IndexCount, SetDataOptions.Discard);
                }
            }

            result.FrameIndex = frame.Index;
            result.DrawnIndex = -1;

            return result;
        }

        private void UpdateZRange () {
            float minZ = Environment.GroundZ;
            float maxZ = minZ;

            foreach (var hv in Environment.HeightVolumes) {
                minZ = Math.Min(minZ, hv.Height);
                maxZ = Math.Max(maxZ, hv.Height);
            }

            Configuration.ZOffset = -minZ;
            Configuration.ZScale = 1.0f / (maxZ - minZ);
        }

        PointLightVertex MakePointLightVertex (LightSource lightSource, float intensityScale) {
            var vertex = new PointLightVertex();
            vertex.LightCenter = lightSource.Position;
            vertex.Color = lightSource.Color;
            vertex.Color.W *= (lightSource.Opacity * intensityScale);
            vertex.Ramp = new Vector2(lightSource.RampStart, lightSource.RampEnd);
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
            UpdateZRange();

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

            var needStencilClear = true;
            int vertexOffset = 0, indexOffset = 0;
            LightSource batchFirstLightSource = null;
            BatchGroup currentLightGroup = null;

            int layerIndex = 0;

            using (var sortedLights = BufferPool<LightSource>.Allocate(Environment.LightSources.Count))
            using (var resultGroup = BatchGroup.New(container, layer, before: BeginLightPass, after: EndLightPass)) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(resultGroup, -9999, "Frame {0:0000} : LightingRenderer {1:X4} : Begin", frame.Index, this.GetHashCode());

                int i = 0;
                var lightCount = 0;

                foreach (var lightSource in Environment.LightSources) {
                    if (IsLightInvisible(lightSource))
                        continue;

                    sortedLights.Data[i++] = lightSource;
                    lightCount += 1;
                }

                Array.Sort(sortedLights.Data, 0, lightCount, LightSourceComparerInstance);

                int lightGroupIndex = 1;

                for (i = 0; i < lightCount; i++) {
                    var lightSource = sortedLights.Data[i];

                    if (lightSource.Opacity <= 0)
                        continue;

                    var lightBounds = new Bounds((Vector2)lightSource.Position - new Vector2(lightSource.RampEnd), (Vector2)lightSource.Position + new Vector2(lightSource.RampEnd));

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
                            (needStencilClear) ||
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
                        currentLightGroup = BatchGroup.New(resultGroup, lightGroupIndex++, before: RestoreScissorRect);

                    Bounds clippedLightBounds;
                    if (lightSource.ClipRegion.HasValue) {
                        var clipBounds = lightSource.ClipRegion.Value;
                        if (!lightBounds.Intersection(ref lightBounds, ref clipBounds, out clippedLightBounds))
                            continue;
                    } else {
                        clippedLightBounds = lightBounds;
                    }

                    if (needStencilClear) {
                        if (Render.Tracing.RenderTrace.EnableTracing)
                            Render.Tracing.RenderTrace.Marker(currentLightGroup, layerIndex++, "Frame {0:0000} : LightingRenderer {1:X4} : Stencil Clear", frame.Index, this.GetHashCode());
#if SHADOW_VIZ
                        ClearBatch.AddNew(currentLightGroup, layerIndex++, Materials.Clear, clearColor: Color.Black, clearStencil: StencilFalse);
#else
                        ClearBatch.AddNew(currentLightGroup, layerIndex++, IlluminantMaterials.ClearStencil, clearStencil: StencilFalse);
#endif
                        needStencilClear = false;
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

                // FIXME
                if (Configuration.TwoPointFiveD) {
                    if (Render.Tracing.RenderTrace.EnableTracing)
                        Render.Tracing.RenderTrace.Marker(resultGroup, layerIndex++, "Frame {0:0000} : LightingRenderer {1:X4} : Volume Faces", frame.Index, this.GetHashCode());

                    RenderTwoPointFiveD(layerIndex, resultGroup);
                }

                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(resultGroup, 9999, "Frame {0:0000} : LightingRenderer {1:X4} : End", frame.Index, this.GetHashCode());
            }
        }
        
        private bool IsLightInvisible (LightSource ls) {
            if (ls.Position.Z < Environment.GroundZ)
                return true;

            var posXy = new Vector2(ls.Position.X, ls.Position.Y);

            var isEnclosed = Environment.HeightVolumes.Any(
                hv => 
                    (hv.ZBase <= ls.Position.Z) &&
                    ((hv.ZBase + hv.Height) >= ls.Position.Z) &&
                    Geometry.PointInPolygon(posXy, hv.Polygon)
            );

            // FIXME: This is broken when lying on the same axis as a polygon edge, sometimes. :|
            // if (isEnclosed)
            //    return true;

            return false;
        }

        const int FaceMaxLights = 3;

        // HACK
        private bool      _DistanceFieldReady = false;

        private int       _VisibleLightCount  = 0;
        private Vector3[] _LightPositions     = new Vector3[FaceMaxLights];
        private Vector3[] _LightProperties    = new Vector3[FaceMaxLights];
        private Vector4[] _LightNeutralColors = new Vector4[FaceMaxLights];
        private Vector4[] _LightColors        = new Vector4[FaceMaxLights];

        private void SetTwoPointFiveDParametersInner (EffectParameterCollection p, bool setTerrainTexture, bool setDistanceTexture) {
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
                1f / Configuration.MaximumRenderSize.First, 
                1f / Configuration.MaximumRenderSize.Second
            );
            p["HeightmapInvScaleFactor"].SetValue(1f / Configuration.HeightmapResolution);
            p["TerrainTextureTexelSize"].SetValue(tsize);

            if (setTerrainTexture)
                p["TerrainTexture"].SetValue(_TerrainDepthmap);

            SetDistanceFieldParameters(p, setDistanceTexture);
        }

        private void SetDistanceFieldParameters (EffectParameterCollection p, bool setDistanceTexture) {
            p["ZDistanceScale"].SetValue(Environment.ZDistanceScale);

            p["DistanceFieldTextureSliceSize"].SetValue(new Vector2(1f / DistanceFieldSlicesX, 1f / DistanceFieldSlicesY));
            p["DistanceFieldTextureSliceCount"].SetValue(new Vector3(DistanceFieldSlicesX, DistanceFieldSlicesY, Configuration.DistanceFieldSliceCount));

            var tsize = new Vector2(
                1f / (Configuration.MaximumRenderSize.First * DistanceFieldSlicesX), 
                1f / (Configuration.MaximumRenderSize.Second * DistanceFieldSlicesY)
            );
            p["DistanceFieldTextureTexelSize"].SetValue(tsize);
            p["DistanceFieldInvScaleFactor"].SetValue(1f / Configuration.DistanceFieldResolution);
            p["DistanceFieldMinimumStepSize"].SetValue(Configuration.DistanceFieldStepSize);
            p["DistanceFieldOcclusionToOpacityPower"].SetValue(Configuration.DistanceFieldOcclusionToOpacityPower);
            p["DistanceFieldMaxConeRadius"].SetValue(Configuration.DistanceFieldMaxConeRadius);
            p["DistanceFieldConeGrowthRate"].SetValue(Configuration.DistanceFieldConeGrowthRate);

            if (setDistanceTexture)
                p["DistanceFieldTexture"].SetValue(_DistanceField);
        }

        private void SetTwoPointFiveDParameters (DeviceManager dm, object _) {
            var frontFaceMaterial = IlluminantMaterials.VolumeFrontFace;
            var topFaceMaterial   = IlluminantMaterials.VolumeTopFace;

            SetTwoPointFiveDParametersInner(frontFaceMaterial.Effect.Parameters, true, true);
            SetTwoPointFiveDParametersInner(topFaceMaterial  .Effect.Parameters, true, true);
        }

        private void RenderTwoPointFiveD (int layerIndex, BatchGroup resultGroup) {
            // FIXME: Support more than 12 lights

            int i = 0;
            foreach (var ls in Environment.LightSources) {
                if (i >= FaceMaxLights)
                    break;
                else if (IsLightInvisible(ls))
                    continue;

                _LightPositions[i]     = ls.Position;
                _LightNeutralColors[i] = ls.NeutralColor;
                _LightColors[i]        = ls.Color;
                _LightProperties[i]    = new Vector3(
                    ls.RampStart, ls.RampEnd,
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
                resultGroup, layerIndex,
                before: (dm, _) => dm.PushStates(),
                after: (dm, _) => dm.PopStates()
            )) {
                ClearBatch.AddNew(
                    group, 0, Materials.Clear, clearZ: 0f
                );

                using (var topBatch = PrimitiveBatch<VertexPositionColor>.New(
                    group, 1, Materials.Get(
                        IlluminantMaterials.VolumeTopFace,                    
                        depthStencilState: TopFaceDepthStencilState,
                        rasterizerState: RasterizerState.CullNone,
                        blendState: BlendState.Opaque
                    ),
                    batchSetup: SetTwoPointFiveDParameters
                ))
                using (var frontBatch = PrimitiveBatch<FrontFaceVertex>.New(
                    group, 2, Materials.Get(
                        IlluminantMaterials.VolumeFrontFace,
                        depthStencilState: FrontFaceDepthStencilState,
                        rasterizerState: RasterizerState.CullNone,
                        blendState: BlendState.Opaque
                    ),
                    batchSetup: SetTwoPointFiveDParameters
                )) {
                    foreach (var volume in Environment.HeightVolumes) {
                        var ffm3d = volume.GetFrontFaceMesh3D();
                        if (ffm3d.Count <= 0)
                            continue;

                        frontBatch.Add(new PrimitiveDrawCall<FrontFaceVertex>(
                            PrimitiveType.TriangleList,
                            ffm3d.Array, ffm3d.Offset, ffm3d.Count / 3
                        ));

                        var m3d = volume.Mesh3D;

                        topBatch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                            PrimitiveType.TriangleList,
                            m3d, 0, m3d.Length / 3
                        ));
                    }
                }
            }
        }

        public void RenderHeightmap (Frame frame, IBatchContainer container, int layer) {
            UpdateZRange();

            Vector2 minCoordinate = new Vector2(999999, 999999);
            Vector2 maxCoordinate = new Vector2(-999999, -999999);

            using (var group = BatchGroup.ForRenderTarget(
                container, layer, _TerrainDepthmap,
                // FIXME: Optimize this
                (dm, _) => {
                    Materials.PushViewTransform(ViewTransform.CreateOrthographic(Configuration.MaximumRenderSize.First, Configuration.MaximumRenderSize.Second));
                },
                (dm, _) => {
                    Materials.PopViewTransform();
                }
            )) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, -1, "Frame {0:0000} : LightingRenderer {1:X4} : Begin Heightmap", frame.Index, this.GetHashCode());

                ClearBatch.AddNew(
                    group, 0, Materials.Clear, 
                    new Color(1.0f, Environment.GroundZ, 0f, 1f)
                );

                using (var minBatch = PrimitiveBatch<VertexPositionColor>.New(
                    group, 1, Materials.ScreenSpaceGeometry,
                    (dm, _) => {
                        dm.Device.RasterizerState = RasterizerState.CullNone;
                        dm.Device.DepthStencilState = DepthStencilState.None;
                        dm.Device.BlendState = HeightMin;
                    }
                ))
                using (var maxBatch = PrimitiveBatch<VertexPositionColor>.New(
                    group, 1, Materials.ScreenSpaceGeometry,
                    (dm, _) => {
                        dm.Device.RasterizerState = RasterizerState.CullNone;
                        dm.Device.DepthStencilState = DepthStencilState.None;
                        dm.Device.BlendState = HeightMax;
                    }
                ))
                // Rasterize the height volumes in sequential order.
                foreach (var hv in Environment.HeightVolumes) {
                    var b = hv.Bounds;
                    minCoordinate.X = Math.Min(minCoordinate.X, b.TopLeft.X);
                    minCoordinate.Y = Math.Min(minCoordinate.Y, b.TopLeft.Y);
                    maxCoordinate.X = Math.Max(maxCoordinate.X, b.BottomRight.X);
                    maxCoordinate.Y = Math.Max(maxCoordinate.Y, b.BottomRight.Y);

                    var m = hv.Mesh3D;

                    minBatch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                        PrimitiveType.TriangleList,
                        m, 0, m.Length / 3
                    ));
                    maxBatch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                        PrimitiveType.TriangleList,
                        m, 0, m.Length / 3
                    ));
                }

                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(group, 2, "Frame {0:0000} : LightingRenderer {1:X4} : End Heightmap", frame.Index, this.GetHashCode());
            }

            if (!_DistanceFieldReady) {
                RenderDistanceField(ref layer, container);
                _DistanceFieldReady = true;
            }
        }

        private void RenderDistanceField (ref int layerIndex, IBatchContainer resultGroup) {
            var vertexDataTextures = new Dictionary<object, Texture2D>();
            var intParameters = IlluminantMaterials.DistanceFieldInterior.Effect.Parameters;
            var extParameters = IlluminantMaterials.DistanceFieldExterior.Effect.Parameters;

            var indices = new short[] {
                0, 1, 3, 1, 2, 3
            };
            
            using (var rtGroup = BatchGroup.ForRenderTarget(
                resultGroup, layerIndex++, _DistanceField
            )) {
                ClearBatch.AddNew(
                    rtGroup, 0, Materials.Clear, Color.Transparent
                );

                for (var _slice = 0; _slice < Configuration.DistanceFieldSliceCount; _slice++) {
                    int slice = _slice;

                    float sliceZ = (slice / (float)(Configuration.DistanceFieldSliceCount - 1));
                    int displaySlice = slice / 2;
                    var sliceX = (displaySlice % DistanceFieldSlicesX) * DistanceFieldSliceWidth;
                    var sliceY = (displaySlice / DistanceFieldSlicesX) * DistanceFieldSliceHeight;
                    var sliceXVirtual = (displaySlice % DistanceFieldSlicesX) * Configuration.MaximumRenderSize.First;
                    var sliceYVirtual = (displaySlice / DistanceFieldSlicesX) * Configuration.MaximumRenderSize.Second;

                    using (var group = BatchGroup.New(rtGroup, slice + 1,
                        // FIXME: Optimize this
                        (dm, _) => {                            
                            var vt = ViewTransform.CreateOrthographic(
                                Configuration.MaximumRenderSize.First * DistanceFieldSlicesX,
                                Configuration.MaximumRenderSize.Second * DistanceFieldSlicesY
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
                        },
                        (dm, _) => {
                            Materials.PopViewTransform();
                        }
                    )) {
                        if (Render.Tracing.RenderTrace.EnableTracing)
                            Render.Tracing.RenderTrace.Marker(group, -1, "LightingRenderer {0:X4} : Begin Distance Field Slice #{1}", this.GetHashCode(), slice);

                        int i = 1;

                        // Rasterize the height volumes in sequential order.
                        // FIXME: Depth buffer/stencil buffer tricks should work for generating this SDF, but don't?
                        using (var interiorGroup = BatchGroup.ForRenderTarget(group, 1, _DistanceField, (dm, _) => {
                            dm.Device.RasterizerState = RenderStates.ScissorOnly;
                            dm.Device.DepthStencilState = DepthStencilState.None;
                        }))
                        using (var exteriorGroup = BatchGroup.ForRenderTarget(group, 2, _DistanceField, (dm, _) => {
                            dm.Device.RasterizerState = RenderStates.ScissorOnly;
                            dm.Device.DepthStencilState = DepthStencilState.None;
                        }))
                            foreach (var hv in Environment.HeightVolumes) {
                                var p = hv.Polygon;
                                var m = hv.Mesh3D;
                                var b = hv.Bounds.Expand(DistanceLimit, DistanceLimit);

                                var verts = new VertexPositionColor[] {
                                    new VertexPositionColor(new Vector3(b.TopLeft, 0), Color.White),
                                    new VertexPositionColor(new Vector3(b.TopRight, 0), Color.White),
                                    new VertexPositionColor(new Vector3(b.BottomRight, 0), Color.White),
                                    new VertexPositionColor(new Vector3(b.BottomLeft, 0), Color.White)
                                };

                                Texture2D vertexDataTexture;

                                if (!vertexDataTextures.TryGetValue(p, out vertexDataTexture))
                                lock (Coordinator.CreateResourceLock) {
                                    vertexDataTexture = new Texture2D(Coordinator.Device, p.Count, 1, false, SurfaceFormat.Vector2);
                                    vertexDataTextures[p] = vertexDataTexture;
                                }

                                vertexDataTexture.SetData(p.GetVertices());

                                using (var batch = PrimitiveBatch<VertexPositionColor>.New(
                                    interiorGroup, i, IlluminantMaterials.DistanceFieldInterior,
                                    (dm, _) => {
                                        intParameters["NumVertices"].SetValue(p.Count);
                                        intParameters["VertexDataTexture"].SetValue(vertexDataTexture);
                                        intParameters["SliceZ"].SetValue(sliceZ);
                                        intParameters["MinZ"].SetValue(hv.ZBase);
                                        intParameters["MaxZ"].SetValue(hv.ZBase + hv.Height);
                                        IlluminantMaterials.DistanceFieldInterior.Flush();
                                    }
                                ))
                                    batch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                                        PrimitiveType.TriangleList,
                                        m, 0, m.Length / 3
                                    ));


                                using (var batch = PrimitiveBatch<VertexPositionColor>.New(
                                    exteriorGroup, i, IlluminantMaterials.DistanceFieldExterior,
                                    (dm, _) => {
                                        extParameters["NumVertices"].SetValue(p.Count);
                                        extParameters["VertexDataTexture"].SetValue(vertexDataTexture);
                                        extParameters["SliceZ"].SetValue(sliceZ);
                                        extParameters["MinZ"].SetValue(hv.ZBase);
                                        extParameters["MaxZ"].SetValue(hv.ZBase + hv.Height);
                                        IlluminantMaterials.DistanceFieldExterior.Flush();
                                    }
                                ))
                                    batch.Add(new PrimitiveDrawCall<VertexPositionColor>(
                                        PrimitiveType.TriangleList,
                                        verts, 0, verts.Length, indices, 0, indices.Length / 3
                                    ));

                                i++;
                            }

                        if (Render.Tracing.RenderTrace.EnableTracing)
                            Render.Tracing.RenderTrace.Marker(group, 2, "LightingRenderer {0:X4} : End Distance Field Slice #{1}", this.GetHashCode(), slice);
                    }
                }
            }

            foreach (var kvp in vertexDataTextures)
                Coordinator.DisposeResource(kvp.Value);
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

        public void RenderOutlines (IBatchContainer container, int layer, bool showLights, Color? lineColor = null, Color? lightColor = null) {
            using (var group = BatchGroup.New(container, layer)) {
                using (var gb = GeometryBatch.New(group, 0, IlluminantMaterials.DebugOutlines)) {
                    VisualizerLineWriterInstance.Batch = gb;

                    var lc = lineColor.GetValueOrDefault(Color.White);
                    foreach (var hv in Environment.HeightVolumes) {
                        VisualizerLineWriterInstance.Color = lc * hv.Height;
                        hv.GenerateLines(VisualizerLineWriterInstance);
                    }

                    VisualizerLineWriterInstance.Color = lineColor.GetValueOrDefault(Color.White);
                    foreach (var lo in Environment.Obstructions)
                        lo.GenerateLines(VisualizerLineWriterInstance);

                    VisualizerLineWriterInstance.Batch = null;
                }

                int i = 0;

                if (showLights)
                foreach (var lightSource in Environment.LightSources) {
                    var cMax = lightColor.GetValueOrDefault(Color.White);
                    var cMin = cMax * 0.25f;

                    using (var gb = GeometryBatch.New(group, i + 1, IlluminantMaterials.DebugOutlines)) {
                        gb.AddFilledRing((Vector2)lightSource.Position, 0f, 2f, cMax, cMax);
                        gb.AddFilledRing((Vector2)lightSource.Position, lightSource.RampStart - 1f, lightSource.RampStart + 1f, cMax, cMax);
                        gb.AddFilledRing((Vector2)lightSource.Position, lightSource.RampEnd - 1f, lightSource.RampEnd + 1f, cMin, cMin);
                    }

                    i += 1;
                }
            }
        }
    }

    public class IlluminantMaterials {
        public readonly DefaultMaterialSet MaterialSet;

        public Material DebugOutlines, Shadow, ClearStencil;
        public Material PointLightLinear, PointLightExponential, PointLightLinearRampTexture, PointLightExponentialRampTexture;
        public Squared.Render.EffectMaterial VolumeFrontFace, VolumeTopFace;
        public Squared.Render.EffectMaterial DistanceFieldExterior, DistanceFieldInterior, VisualizeDistanceField;
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
