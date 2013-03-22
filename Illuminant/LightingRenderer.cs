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

            public void Write (Vector2 a, Vector2 b) {
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

            public void Write (Vector2 a, Vector2 b) {
                Batch.AddLine(a, b, Color);
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
                int result = lhs.Mode.GetHashCode().CompareTo(rhs.Mode.GetHashCode());
                if (result == 0)
                    result = lhs.NeutralColor.GetHashCode().CompareTo(rhs.NeutralColor.GetHashCode());
                if (result == 0)
                    result = (lhs.ClipRegion.HasValue ? 1 : 0).CompareTo(rhs.ClipRegion.HasValue ? 1 : 0);

                return result;
            }
        }

        private struct PointLightRecord {
            public int VertexOffset, IndexOffset, VertexCount, IndexCount;
        }

        public readonly DefaultMaterialSet Materials;
        public readonly Squared.Render.EffectMaterial ShadowMaterialInner, PointLightMaterialInner;
        public readonly Material DebugOutlines, Shadow, PointLight, ClearStencil;
        public readonly DepthStencilState PointLightStencil, ShadowStencil;

        private readonly ArrayLineWriter ArrayLineWriterInstance = new ArrayLineWriter();
        private readonly VisualizerLineWriter VisualizerLineWriterInstance = new VisualizerLineWriter();
        private readonly LightSourceComparer LightSourceComparerInstance = new LightSourceComparer();

        private PointLightVertex[] PointLightVertices = new PointLightVertex[128];
        private short[] PointLightIndices = null;
        private readonly Dictionary<Pair<int>, CachedSector> SectorCache = new Dictionary<Pair<int>, CachedSector>(new IntPairComparer());
        private readonly List<PointLightRecord> PointLightBatchBuffer = new List<PointLightRecord>(128);
        private Rectangle StoredScissorRect;

        public static readonly short[] ShadowIndices;

        public LightingEnvironment Environment;

        static LightingRenderer () {
            ShadowIndices = new short[] {
                0, 1, 2,
                1, 2, 3
            };
        }

        public LightingRenderer (ContentManager content, DefaultMaterialSet materials, LightingEnvironment environment) {
            Materials = materials;

            ClearStencil = materials.Clear;

            materials.Add(
                DebugOutlines = materials.WorldSpaceGeometry.SetStates(
                    blendState: BlendState.AlphaBlend
                )
            );

            PointLightStencil = new DepthStencilState {
                DepthBufferEnable = false,
                StencilEnable = true,
                StencilWriteMask = 0,
                StencilFunction = CompareFunction.Equal,
                StencilPass = StencilOperation.Keep,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = 0
            };

            materials.Add(PointLight = new DelegateMaterial(
                PointLightMaterialInner = new Squared.Render.EffectMaterial(
                    content.Load<Effect>("Illumination"), "PointLight"
                ),
                new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RenderStates.ScissorOnly, depthStencilState: PointLightStencil
                    )
                },
                new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone, depthStencilState: DepthStencilState.None
                    )
                }
            ));

            ShadowStencil = new DepthStencilState {
                DepthBufferEnable = false,
                StencilEnable = true,
                StencilWriteMask = Int32.MaxValue,
                StencilFunction = CompareFunction.Never,
                StencilPass = StencilOperation.Zero,
                StencilFail = StencilOperation.Replace,
                ReferenceStencil = 1
            };

            materials.Add(Shadow = new DelegateMaterial(
                ShadowMaterialInner = new Squared.Render.EffectMaterial(
                    content.Load<Effect>("Illumination"), "Shadow"
                ),
                new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RenderStates.ScissorOnly,
                        depthStencilState: ShadowStencil,
                        blendState: BlendState.Opaque
                    )
                },
                new[] {
                    MaterialUtil.MakeDelegate(
                        rasterizerState: RasterizerState.CullNone, depthStencilState: DepthStencilState.None
                    )
                }
            ));
            
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

        private void StoreScissorRect (DeviceManager device, object userData) {
            StoredScissorRect = device.Device.ScissorRectangle;
        }

        private void RestoreScissorRect (DeviceManager device, object userData) {
            device.Device.ScissorRectangle = StoredScissorRect;
        }

        private Rectangle GetScissorRectForLightSource (LightSource ls) {
            Rectangle scissor;

            if (ls.ClipRegion.HasValue) {
                // FIXME: ViewportPosition
                var clipRegion = ls.ClipRegion.Value;
                scissor = new Rectangle(
                    (int)Math.Floor(clipRegion.TopLeft.X * Materials.ViewportScale.X),
                    (int)Math.Floor(clipRegion.TopLeft.Y * Materials.ViewportScale.Y),
                    (int)Math.Ceiling(clipRegion.Size.X * Materials.ViewportScale.X),
                    (int)Math.Ceiling(clipRegion.Size.Y * Materials.ViewportScale.Y)
                );
            } else {
                scissor = new Rectangle(
                    (int)Math.Floor((ls.Position.X - ls.RampEnd - Materials.ViewportPosition.X) * Materials.ViewportScale.X),
                    (int)Math.Floor((ls.Position.Y - ls.RampEnd - Materials.ViewportPosition.Y) * Materials.ViewportScale.Y),
                    (int)Math.Ceiling(ls.RampEnd * 2 * Materials.ViewportScale.X),
                    (int)Math.Ceiling(ls.RampEnd * 2 * Materials.ViewportScale.Y)
                );
            }

            var result = Rectangle.Intersect(scissor, StoredScissorRect);
            return result;
        }

        private void IlluminationBatchSetup (DeviceManager device, object lightSource) {
            var ls = (LightSource)lightSource;

            switch (ls.Mode) {
                case LightSourceMode.Additive:
                    device.Device.BlendState = BlendState.Additive;
                    break;
                case LightSourceMode.Subtractive:
                    device.Device.BlendState = RenderStates.SubtractiveBlend;
                    break;
                case LightSourceMode.Alpha:
                    device.Device.BlendState = BlendState.AlphaBlend;
                    break;
                case LightSourceMode.Replace:
                    device.Device.BlendState = BlendState.Opaque;
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

            PointLightMaterialInner.Effect.Parameters["LightNeutralColor"].SetValue(ls.NeutralColor);

            device.Device.ScissorRectangle = StoredScissorRect;
        }

        private void ShadowBatchSetup (DeviceManager device, object lightSource) {
            var ls = (LightSource)lightSource;

            ShadowMaterialInner.Effect.Parameters["LightCenter"].SetValue(ls.Position);
            ShadowMaterialInner.Effect.Parameters["ShadowLength"].SetValue(ls.RampEnd * 2f);

            device.Device.ScissorRectangle = GetScissorRectForLightSource(ls);
        }

        private CachedSector GetCachedSector (Frame frame, Pair<int> sectorIndex) {
            CachedSector result;
            if (!SectorCache.TryGetValue(sectorIndex, out result))
                SectorCache.Add(sectorIndex, result = new CachedSector {
                    SectorIndex = sectorIndex
                });

            if (result.FrameIndex == frame.Index)
                return result;

            var sector = Environment.Obstructions[sectorIndex];

            int lineCount = 0;
            foreach (var item in sector)
                lineCount += item.Item.LineCount;

            result.PrimitiveCount = lineCount * 2;
            result.VertexCount = lineCount * 4;
            result.IndexCount = lineCount * 6;

            if ((result.ObstructionVertexBuffer != null) && (result.ObstructionVertexBuffer.VertexCount < result.VertexCount)) {
                result.ObstructionVertexBuffer.Dispose();
                result.ObstructionVertexBuffer = null;
            }

            if ((result.ObstructionIndexBuffer != null) && (result.ObstructionIndexBuffer.IndexCount < result.IndexCount)) {
                result.ObstructionIndexBuffer.Dispose();
                result.ObstructionIndexBuffer = null;
            }

            if (result.ObstructionVertexBuffer == null)
                result.ObstructionVertexBuffer = new DynamicVertexBuffer(frame.RenderManager.DeviceManager.Device, (new ShadowVertex().VertexDeclaration), result.VertexCount, BufferUsage.WriteOnly);

            if (result.ObstructionIndexBuffer == null)
                result.ObstructionIndexBuffer = new DynamicIndexBuffer(frame.RenderManager.DeviceManager.Device, IndexElementSize.SixteenBits, result.IndexCount, BufferUsage.WriteOnly);

            using (var va = BufferPool<ShadowVertex>.Allocate(result.VertexCount))
            using (var ia = BufferPool<short>.Allocate(result.IndexCount)) {
                var vb = va.Data;
                var ib = ia.Data;

                ArrayLineWriterInstance.SetOutput(vb, ib);

                foreach (var itemInfo in sector)
                    itemInfo.Item.GenerateLines(ArrayLineWriterInstance);

                var linesWritten = ArrayLineWriterInstance.Finish();
                if (linesWritten != lineCount)
                    throw new InvalidDataException("GenerateLines didn't generate enough lines based on LineCount");

                result.ObstructionVertexBuffer.SetData(vb, 0, result.VertexCount, SetDataOptions.Discard);
                result.ObstructionIndexBuffer.SetData(ib, 0, result.IndexCount, SetDataOptions.Discard);
            }

            result.FrameIndex = frame.Index;

            return result;
        }

        public void RenderLighting (Frame frame, IBatchContainer container, int layer) {
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
                var lightCount = Environment.LightSources.Count;
                Environment.LightSources.CopyTo(sortedLights.Data);
                Array.Sort(sortedLights.Data, 0, lightCount, LightSourceComparerInstance);

                int lightGroupIndex = 1;

                for (var i = 0; i < lightCount; i++) {
                    var lightSource = sortedLights.Data[i];

                    if (batchFirstLightSource != null) {
                        var needFlush =
                            (needStencilClear) ||
                            (batchFirstLightSource.ClipRegion.HasValue != lightSource.ClipRegion.HasValue) ||
                            (batchFirstLightSource.NeutralColor != lightSource.NeutralColor) ||
                            (batchFirstLightSource.Mode != lightSource.Mode);

                        if (needFlush) {
                            FlushPointLightBatch(ref currentLightGroup, ref batchFirstLightSource, ref layerIndex);
                            indexOffset = 0;
                        }
                    }

                    if (batchFirstLightSource == null)
                        batchFirstLightSource = lightSource;
                    if (currentLightGroup == null)
                        currentLightGroup = BatchGroup.New(resultGroup, lightGroupIndex++, before: RestoreScissorRect);

                    var lightBounds = new Bounds(lightSource.Position - new Vector2(lightSource.RampEnd), lightSource.Position + new Vector2(lightSource.RampEnd));

                    Bounds clippedLightBounds;
                    if (lightSource.ClipRegion.HasValue) {
                        var clipBounds = lightSource.ClipRegion.Value;
                        if (!lightBounds.Intersection(ref lightBounds, ref clipBounds, out clippedLightBounds))
                            continue;
                    } else {
                        clippedLightBounds = lightBounds;
                    }

                    if (needStencilClear) {
                        ClearBatch.AddNew(currentLightGroup, layerIndex++, ClearStencil, clearStencil: 0);
                        needStencilClear = false;
                    }

                    NativeBatch stencilBatch = null;
                    SpatialCollection<LightObstructionBase>.Sector currentSector;
                    using (var e = Environment.Obstructions.GetSectorsFromBounds(lightBounds))
                    while (e.GetNext(out currentSector)) {
                        var cachedSector = GetCachedSector(frame, currentSector.Index);
                        if (cachedSector.VertexCount <= 0)
                            continue;

                        if (stencilBatch == null) {
                            stencilBatch = NativeBatch.New(currentLightGroup, layerIndex++, Shadow, ShadowBatchSetup, lightSource);
                            stencilBatch.Dispose();
                            needStencilClear = true;
                        }

                        stencilBatch.Add(new NativeDrawCall(
                            PrimitiveType.TriangleList, cachedSector.ObstructionVertexBuffer, 0, cachedSector.ObstructionIndexBuffer, 0, 0, cachedSector.VertexCount, 0, cachedSector.PrimitiveCount
                        ));
                    }

                    PointLightVertex vertex;

                    vertex.LightCenter = lightSource.Position;
                    vertex.Color = lightSource.Color;
                    vertex.Color *= lightSource.Opacity;
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

                FlushPointLightBatch(ref currentLightGroup, ref batchFirstLightSource, ref layerIndex);
            }
        }

        private void FlushPointLightBatch (ref BatchGroup lightGroup, ref LightSource batchFirstLightSource, ref int layerIndex) {
            if (lightGroup == null)
                return;

            using (var pb = PrimitiveBatch<PointLightVertex>.New(lightGroup, layerIndex++, PointLight, IlluminationBatchSetup, batchFirstLightSource)) {
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
                using (var gb = GeometryBatch.New(group, 0, DebugOutlines)) {
                    VisualizerLineWriterInstance.Batch = gb;
                    VisualizerLineWriterInstance.Color = lineColor.GetValueOrDefault(Color.White);

                    foreach (var lo in Environment.Obstructions)
                        lo.GenerateLines(VisualizerLineWriterInstance);

                    VisualizerLineWriterInstance.Batch = null;
                }

                if (showLights)
                for (var i = 0; i < Environment.LightSources.Count; i++) {
                    var lightSource = Environment.LightSources[i];

                    var cMax = lightColor.GetValueOrDefault(Color.White);
                    var cMin = cMax * 0.25f;

                    using (var gb = GeometryBatch.New(group, i + 1, DebugOutlines)) {
                        gb.AddFilledRing(lightSource.Position, 0f, 2f, cMax, cMax);
                        gb.AddFilledRing(lightSource.Position, lightSource.RampStart - 1f, lightSource.RampStart + 1f, cMax, cMax);
                        gb.AddFilledRing(lightSource.Position, lightSource.RampEnd - 1f, lightSource.RampEnd + 1f, cMin, cMin);
                    }
                }
            }
        }
    }
}
