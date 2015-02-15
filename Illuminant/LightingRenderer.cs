using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingRenderer : IDisposable {
        private class ArrayLineWriter : ILineWriter {
            private int _Count = 0;
            private ShadowVertex[] VertexBuffer;
            private short[] IndexBuffer;

            public void Write (LightPosition a, LightPosition b) {
                ShadowVertex vertex;
                int vertexOffset = _Count * 4;
                int indexOffset = _Count * 6;

                vertex.Position = a;
                vertex.PairIndex = 0;
                VertexBuffer[vertexOffset + 0] = vertex;
                vertex.PairIndex = 1;
                VertexBuffer[vertexOffset + 1] = vertex;

                vertex.Position = b;
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

            public void Write (LightPosition a, LightPosition b) {
                Batch.AddLine(
                    (Vector2)a, 
                    (Vector2)b, 
                    Color
                );
            }
        }

        public class CachedSector : IDisposable {
            public Pair<int> SectorIndex;
            public int FrameIndex;
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
        public float ClipRegionScale = 1f;

        public readonly DefaultMaterialSet Materials;
        public readonly RenderCoordinator Coordinator;
        public readonly IlluminantMaterials IlluminantMaterials;
        public readonly Squared.Render.EffectMaterial ShadowMaterialInner;
        public readonly Squared.Render.EffectMaterial[] PointLightMaterialsInner = new Squared.Render.EffectMaterial[4];
        public readonly DepthStencilState PointLightStencil, ShadowStencil;

        private static readonly Dictionary<TextureFilter, SamplerState> RampSamplerStates = new Dictionary<TextureFilter, SamplerState>();

        private readonly ArrayLineWriter ArrayLineWriterInstance = new ArrayLineWriter();
        private readonly VisualizerLineWriter VisualizerLineWriterInstance = new VisualizerLineWriter();
        private readonly LightSourceComparer LightSourceComparerInstance = new LightSourceComparer();

        private PointLightVertex[] PointLightVertices = new PointLightVertex[128];
        private short[] PointLightIndices = null;
        private readonly Dictionary<Pair<int>, CachedSector> SectorCache = new Dictionary<Pair<int>, CachedSector>(new IntPairComparer());
        private readonly List<PointLightRecord> PointLightBatchBuffer = new List<PointLightRecord>(128);
        private Rectangle StoredScissorRect;

        public readonly Pair<int> MaximumRenderSize;

        public static readonly short[] ShadowIndices;

        public LightingEnvironment Environment;

        private readonly Action<DeviceManager, object> StoreScissorRect, RestoreScissorRect, ShadowBatchSetup, IlluminationBatchSetup;

        static LightingRenderer () {
            ShadowIndices = new short[] {
                0, 1, 2,
                1, 2, 3
            };
        }

        private readonly RenderTarget2D TerrainDepthmap;
        private readonly RenderTarget2D ShadowDepthmap;

        const int StencilTrue = 0xFF;
        const int StencilFalse = 0x00;

        public LightingRenderer (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, LightingEnvironment environment,
            int maxWidth, int maxHeight
        ) {
            Materials = materials;
            Coordinator = coordinator;

            IlluminantMaterials = new IlluminantMaterials(materials);

            StoreScissorRect = _StoreScissorRect;
            RestoreScissorRect = _RestoreScissorRect;
            ShadowBatchSetup = _ShadowBatchSetup;
            IlluminationBatchSetup = _IlluminationBatchSetup;

            MaximumRenderSize = new Pair<int>(maxWidth, maxHeight);

            lock (coordinator.CreateResourceLock) {
                // HACK for debugging visualization purposes
                var fmt = SurfaceFormat.Rgba64;

                TerrainDepthmap = new RenderTarget2D(
                    coordinator.Device, maxWidth, maxHeight,
                    false, fmt, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.DiscardContents
                );

                ShadowDepthmap = new RenderTarget2D(
                    coordinator.Device, maxWidth, maxHeight,
                    false, fmt, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.DiscardContents
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

            // If stencil == false, paint point light at this location
            PointLightStencil = new DepthStencilState {
                DepthBufferEnable = false,
                StencilEnable = true,
                StencilMask = StencilTrue,
                StencilWriteMask = StencilFalse,
                StencilFunction = CompareFunction.Equal,
                StencilPass = StencilOperation.Keep,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = StencilFalse
            };

            {
                var dBegin = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RenderStates.ScissorOnly, depthStencilState: PointLightStencil
                    )
                };
                var dEnd = new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone, depthStencilState: DepthStencilState.None
                    )
                };

#if SDL2
                materials.Add(IlluminantMaterials.PointLightExponential = new DelegateMaterial(
                    PointLightMaterialsInner[0] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("PointLightExponential"), "PointLightExponential"
                    ), dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.PointLightLinear = new DelegateMaterial(
                    PointLightMaterialsInner[1] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("PointLightLinear"), "PointLightLinear"
                    ), dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.PointLightExponentialRampTexture = new DelegateMaterial(
                    PointLightMaterialsInner[2] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("PointLightExponentialRampTexture"), "PointLightExponentialRampTexture"
                    ), dBegin, dEnd
                ));

                materials.Add(IlluminantMaterials.PointLightLinearRampTexture = new DelegateMaterial(
                    PointLightMaterialsInner[3] = new Squared.Render.EffectMaterial(
                        content.Load<Effect>("PointLightLinearRampTexture"), "PointLightLinearRampTexture"
                    ), dBegin, dEnd
                ));
#else
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
            }

            // If stencil == false: set stencil to true.
            // If stencil == true: leave stencil alone, don't paint this pixel
            ShadowStencil = new DepthStencilState {
                DepthBufferEnable = false,
                StencilEnable = true,
                StencilMask = StencilTrue,
                StencilWriteMask = StencilTrue,
                StencilFunction = CompareFunction.NotEqual,
                StencilPass = StencilOperation.Replace,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = StencilTrue
            };

            materials.Add(IlluminantMaterials.Shadow = new DelegateMaterial(
                ShadowMaterialInner = new Squared.Render.EffectMaterial(
#if SDL2
                    content.Load<Effect>("Shadow"), "Shadow"
#else
                    content.Load<Effect>("Illumination"), "Shadow"
#endif
                ),
                new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RenderStates.ScissorOnly,
                        depthStencilState: ShadowStencil,
                        blendState: RenderStates.DrawNone
                    )
                },
                new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone, depthStencilState: DepthStencilState.None
                    )
                }
            ));

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

            PointLightStencil.Dispose();
            ShadowStencil.Dispose();
        }

        public Texture2D Depthmap {
            get {
                return TerrainDepthmap;
            }
        }

        private void _StoreScissorRect (DeviceManager device, object userData) {
            StoredScissorRect = device.Device.ScissorRectangle;
        }

        private void _RestoreScissorRect (DeviceManager device, object userData) {
            device.Device.ScissorRectangle = StoredScissorRect;
        }

        private Rectangle GetScissorRectForLightSource (DeviceManager device, LightSource ls) {
            Bounds scissorBounds;

            // FIXME: Replace this with a use of the material set's modelview/projection matrix and device
            //  viewport to 'project' the clip region to scissor coordinates?
            var scale = new Vector2(
                Materials.ViewportScale.X * ClipRegionScale,
                Materials.ViewportScale.Y * ClipRegionScale
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
            }

            device.Device.SamplerStates[1] = GetRampSamplerState(ls.RampTextureFilter);
            device.Device.ScissorRectangle = StoredScissorRect;
        }

        private void _ShadowBatchSetup (DeviceManager device, object lightSource) {
            var ls = (LightSource)lightSource;

            ShadowMaterialInner.Effect.Parameters["LightCenter"].SetValue((Vector3)ls.Position);
            
            // FIXME: Figure out why the derived computation for this in the shader doesn't work.
            // ShadowMaterialInner.Effect.Parameters["ShadowLength"].SetValue(ls.RampEnd * 2);

            // HACK: 10k pixels should be more than enough
            const float shadowLength = 9999f;
            ShadowMaterialInner.Effect.Parameters["ShadowLength"].SetValue(shadowLength);

            device.Device.ScissorRectangle = GetScissorRectForLightSource(device, ls);
        }

        private CachedSector GetCachedSector (Frame frame, Pair<int> sectorIndex) {
            CachedSector result;
            if (!SectorCache.TryGetValue(sectorIndex, out result))
                SectorCache.Add(sectorIndex, result = new CachedSector {
                    SectorIndex = sectorIndex
                });

            if (result.FrameIndex == frame.Index)
                return result;

            SpatialCollection<HeightVolume>.Sector heightSector;
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

            return result;
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

            var needStencilClear = true;
            int vertexOffset = 0, indexOffset = 0;
            LightSource batchFirstLightSource = null;
            BatchGroup currentLightGroup = null;

            int layerIndex = 0;

            using (var sortedLights = BufferPool<LightSource>.Allocate(Environment.LightSources.Count))
            using (var resultGroup = BatchGroup.New(container, layer, before: StoreScissorRect, after: RestoreScissorRect)) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(resultGroup, -9999, "Frame {0:0000} : LightingRenderer {1:X4} : Begin", frame.Index, this.GetHashCode());

                int i = 0;
                var lightCount = Environment.LightSources.Count;

                foreach (var lightSource in Environment.LightSources)
                    sortedLights.Data[i++] = lightSource;

                Array.Sort(sortedLights.Data, 0, lightCount, LightSourceComparerInstance);

                int lightGroupIndex = 1;

                for (i = 0; i < lightCount; i++) {
                    var lightSource = sortedLights.Data[i];

                    if (lightSource.Opacity <= 0)
                        continue;

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

                    var lightBounds = new Bounds((Vector2)lightSource.Position - new Vector2(lightSource.RampEnd), (Vector2)lightSource.Position + new Vector2(lightSource.RampEnd));

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
                        ClearBatch.AddNew(currentLightGroup, layerIndex++, IlluminantMaterials.ClearStencil, clearStencil: StencilFalse);
                        needStencilClear = false;
                    }

                    NativeBatch stencilBatch = null;

                    {
                        SpatialCollection<LightObstructionBase>.Sector currentSector;
                        using (var e = Environment.Obstructions.GetSectorsFromBounds(lightBounds))
                        while (e.GetNext(out currentSector)) {
                            var cachedSector = GetCachedSector(frame, currentSector.Index);
                            if (cachedSector.VertexCount <= 0)
                                continue;

                            RenderLightingSector(frame, ref needStencilClear, currentLightGroup, ref layerIndex, lightSource, ref stencilBatch, cachedSector);
                        }
                    }

                    {
                        SpatialCollection<HeightVolume>.Sector currentSector;
                        using (var e = Environment.HeightVolumes.GetSectorsFromBounds(lightBounds))
                        while (e.GetNext(out currentSector)) {
                            var cachedSector = GetCachedSector(frame, currentSector.Index);
                            if (cachedSector.VertexCount <= 0)
                                continue;

                            RenderLightingSector(frame, ref needStencilClear, currentLightGroup, ref layerIndex, lightSource, ref stencilBatch, cachedSector);
                        }
                    }

                    PointLightVertex vertex;

                    vertex.LightCenter = lightSource.Position;
                    vertex.Color = lightSource.Color;
                    vertex.Color.W *= (lightSource.Opacity * intensityScale);
                    vertex.Ramp = new Vector2(lightSource.RampStart, lightSource.RampEnd);

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

        private void RenderLightingSector (Frame frame, ref bool needStencilClear, BatchGroup currentLightGroup, ref int layerIndex, LightSource lightSource, ref NativeBatch stencilBatch, CachedSector cachedSector) {
            if (stencilBatch == null) {
                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(currentLightGroup, layerIndex++, "Frame {0:0000} : LightingRenderer {1:X4} : Begin Stencil Shadow Batch", frame.Index, this.GetHashCode());

                stencilBatch = NativeBatch.New(currentLightGroup, layerIndex++, IlluminantMaterials.Shadow, ShadowBatchSetup, lightSource);
                stencilBatch.Dispose();
                needStencilClear = true;

                if (Render.Tracing.RenderTrace.EnableTracing)
                    Render.Tracing.RenderTrace.Marker(currentLightGroup, layerIndex++, "Frame {0:0000} : LightingRenderer {1:X4} : End Stencil Shadow Batch", frame.Index, this.GetHashCode());
            }

            stencilBatch.Add(new NativeDrawCall(
                PrimitiveType.TriangleList, cachedSector.ObstructionVertexBuffer, 0, cachedSector.ObstructionIndexBuffer, 0, 0, cachedSector.VertexCount, 0, cachedSector.PrimitiveCount
            ));
        }

        public void RenderHeightmap (Frame frame, IBatchContainer container, int layer) {
            using (var group = BatchGroup.ForRenderTarget(container, layer, TerrainDepthmap)) {
                ClearBatch.AddNew(
                    group, 0, Materials.Clear, 
                    // FIXME: We should write GroundZ to the color channel!!!
                    Color.Transparent, Environment.GroundZ, 0
                );

                using (var pb = PrimitiveBatch<VertexPositionColor>.New(
                    group, 0, Materials.ScreenSpaceGeometry, 
                    (dm, _) => {
                        dm.Device.DepthStencilState = DepthStencilState.None;
                        dm.Device.RasterizerState = RasterizerState.CullNone;
                        dm.Device.BlendState = BlendState.Opaque;
                    }
                ))
                foreach (var hv in Environment.HeightVolumes) {
                    var m = hv.Mesh3D;

                    pb.Add(new PrimitiveDrawCall<VertexPositionColor>(
                        PrimitiveType.TriangleList,
                        m, 0, m.Length / 3
                    ));
                }
            }
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
                    VisualizerLineWriterInstance.Color = lineColor.GetValueOrDefault(Color.White);

                    foreach (var hv in Environment.HeightVolumes)
                        hv.GenerateLines(VisualizerLineWriterInstance);

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
        public Squared.Render.EffectMaterial ScreenSpaceGammaCompressedBitmap, WorldSpaceGammaCompressedBitmap;
        public Squared.Render.EffectMaterial ScreenSpaceToneMappedBitmap, WorldSpaceToneMappedBitmap;
#if !SDL2
        public Squared.Render.EffectMaterial ScreenSpaceRampBitmap, WorldSpaceRampBitmap;
#endif

        internal readonly Effect[] EffectsToSetGammaCompressionParametersOn;
        internal readonly Effect[] EffectsToSetToneMappingParametersOn;
        internal readonly Effect[] EffectsToSetRampTextureOn;

        internal IlluminantMaterials (DefaultMaterialSet materialSet) {
            MaterialSet = materialSet;

            EffectsToSetGammaCompressionParametersOn = new Effect[2];
            EffectsToSetToneMappingParametersOn = new Effect[2];
            EffectsToSetRampTextureOn = new Effect[2];
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
