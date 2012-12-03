using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingRenderer : IDisposable {
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

        public readonly DefaultMaterialSet Materials;
        public readonly Squared.Render.EffectMaterial ShadowMaterialInner;
        public readonly Material DebugOutlines, Shadow, PointLight, ClearStencil, ScreenSpaceLightmappedBitmap, WorldSpaceLightmappedBitmap;
        public readonly DepthStencilState PointLightStencil, ShadowStencil;
        public readonly BlendState SubtractiveBlend, MaxBlend, MinBlend;

        private readonly Dictionary<Pair<int>, CachedSector> SectorCache = new Dictionary<Pair<int>, CachedSector>(new IntPairComparer());
        private Rectangle StoredScissorRect;

        public static readonly short[] ShadowIndices;
        public static readonly short[] PointLightIndices;

        public LightingEnvironment Environment;

        static LightingRenderer () {
            ShadowIndices = new short[] {
                0, 1, 2,
                1, 2, 3
            };

            PointLightIndices = new short[] {
                0, 1, 3,
                1, 2, 3
            };
        }

        public LightingRenderer (ContentManager content, DefaultMaterialSet materials, LightingEnvironment environment) {
            Materials = materials;

            ClearStencil = materials.Clear;

            materials.Add(DebugOutlines = new DelegateMaterial(
                materials.WorldSpaceGeometry,
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => dm.Device.BlendState = BlendState.AlphaBlend
                    )
                },
                new Action<DeviceManager>[0]
            ));

            PointLightStencil = new DepthStencilState {
                DepthBufferEnable = false,
                StencilEnable = true,
                StencilFunction = CompareFunction.Equal,
                StencilPass = StencilOperation.Keep,
                StencilFail = StencilOperation.Keep,
                ReferenceStencil = 1
            };

            materials.Add(PointLight = new DelegateMaterial(
                new Squared.Render.EffectMaterial(
                    content.Load<Effect>("Illumination"), "PointLight"
                ),
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => {
                            dm.Device.DepthStencilState = PointLightStencil;
                            dm.Device.RasterizerState = RasterizerState.CullNone;
                        }
                    )
                },
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => dm.Device.DepthStencilState = DepthStencilState.None
                    )
                }
            ));

            ShadowStencil = new DepthStencilState {
                DepthBufferEnable = false,
                StencilEnable = true,
                StencilFunction = CompareFunction.Never,
                StencilPass = StencilOperation.Keep,
                StencilFail = StencilOperation.Zero
            };

            materials.Add(Shadow = new DelegateMaterial(
                ShadowMaterialInner = new Squared.Render.EffectMaterial(
                    content.Load<Effect>("Illumination"), "Shadow"
                ),
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => {
                            dm.Device.BlendState = BlendState.Opaque;
                            dm.Device.DepthStencilState = ShadowStencil;
                            dm.Device.RasterizerState = RasterizerState.CullNone;
                        }
                    )
                },
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => dm.Device.DepthStencilState = DepthStencilState.None
                    )
                }
            ));

            materials.Add(ScreenSpaceLightmappedBitmap = new DelegateMaterial(
                new Squared.Render.EffectMaterial(
                    content.Load<Effect>("SquaredBitmapShader"), "ScreenSpaceLightmappedBitmap"
                ),
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => {
                            dm.Device.BlendState = BlendState.AlphaBlend;
                        }
                    )
                },
                new Action<DeviceManager>[0]
            ));

            materials.Add(WorldSpaceLightmappedBitmap = new DelegateMaterial(
                new Squared.Render.EffectMaterial(
                    content.Load<Effect>("SquaredBitmapShader"), "WorldSpaceLightmappedBitmap"
                ),
                new[] {
                    (Action<DeviceManager>)(
                        (dm) => {
                            dm.Device.BlendState = BlendState.AlphaBlend;
                        }
                    )
                },
                new Action<DeviceManager>[0]
            ));

            SubtractiveBlend = new BlendState {
                AlphaBlendFunction = BlendFunction.Add,
                AlphaDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.One,
                ColorBlendFunction = BlendFunction.Subtract,
                ColorDestinationBlend = Blend.One,
                ColorSourceBlend = Blend.One
            };

            MaxBlend = new BlendState {
                AlphaBlendFunction = BlendFunction.Add,
                AlphaDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.One,
                ColorBlendFunction = BlendFunction.Max,
                ColorDestinationBlend = Blend.One,
                ColorSourceBlend = Blend.One
            };

            MinBlend = new BlendState {
                AlphaBlendFunction = BlendFunction.Add,
                AlphaDestinationBlend = Blend.One,
                AlphaSourceBlend = Blend.One,
                ColorBlendFunction = BlendFunction.Min,
                ColorDestinationBlend = Blend.One,
                ColorSourceBlend = Blend.One
            };

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

        private void StoreScissorRect (DeviceManager device) {
            StoredScissorRect = device.Device.ScissorRectangle;
        }

        private void RestoreScissorRect (DeviceManager device) {
            device.Device.ScissorRectangle = StoredScissorRect;
        }

        private Rectangle GetScissorRectForLightSource (LightSource ls) {
            var scissor = new Rectangle(
                (int)Math.Floor((ls.Position.X - ls.RampEnd - Materials.ViewportPosition.X) * Materials.ViewportScale.X),
                (int)Math.Floor((ls.Position.Y - ls.RampEnd - Materials.ViewportPosition.Y) * Materials.ViewportScale.Y),
                (int)Math.Ceiling(ls.RampEnd * 2 * Materials.ViewportScale.X),
                (int)Math.Ceiling(ls.RampEnd * 2 * Materials.ViewportScale.Y)
            );

            return Rectangle.Intersect(scissor, StoredScissorRect);
        }

        private void IlluminationBatchSetup (DeviceManager device, object lightSource) {
            var ls = (LightSource)lightSource;

            device.Device.ScissorRectangle = GetScissorRectForLightSource(ls);

            switch (ls.Mode) {
                case LightSourceMode.Additive:
                    device.Device.BlendState = BlendState.Additive;
                    break;
                case LightSourceMode.Subtractive:
                    device.Device.BlendState = SubtractiveBlend;
                    break;
                case LightSourceMode.Alpha:
                    device.Device.BlendState = BlendState.AlphaBlend;
                    break;
                case LightSourceMode.Max:
                    device.Device.BlendState = MaxBlend;
                    break;
                case LightSourceMode.Min:
                    device.Device.BlendState = MinBlend;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("Mode");
            }
        }

        private void ShadowBatchSetup (DeviceManager device, object lightSource) {
            var ls = (LightSource)lightSource;

            ShadowMaterialInner.Effect.Parameters["LightCenter"].SetValue(ls.Position);
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

            result.PrimitiveCount = sector.Count * 2;
            result.VertexCount = sector.Count * 4;
            result.IndexCount = sector.Count * 6;

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

                int i = 0;
                foreach (var itemInfo in sector) {
                    var obstruction = itemInfo.Item;
                    ShadowVertex vertex;
                    int vertexOffset = i * 4;
                    int indexOffset = i * 6;

                    vertex.A = obstruction.A;
                    vertex.B = obstruction.B;

                    vertex.CornerIndex = 0;
                    vb[vertexOffset + 0] = vertex;
                    vertex.CornerIndex = 1;
                    vb[vertexOffset + 1] = vertex;
                    vertex.CornerIndex = 2;
                    vb[vertexOffset + 2] = vertex;
                    vertex.CornerIndex = 3;
                    vb[vertexOffset + 3] = vertex;

                    for (var j = 0; j < ShadowIndices.Length; j++)
                        ib[indexOffset + j] = (short)(vertexOffset + ShadowIndices[j]);

                    i += 1;
                }

                result.ObstructionVertexBuffer.SetData(vb, 0, result.VertexCount, SetDataOptions.Discard);
                result.ObstructionIndexBuffer.SetData(ib, 0, result.IndexCount, SetDataOptions.Discard);
            }

            result.FrameIndex = frame.Index;

            return result;
        }

        public void RenderLighting (Frame frame, IBatchContainer container, int layer) {
            using (var resultGroup = BatchGroup.New(container, layer, before: StoreScissorRect, after: RestoreScissorRect))
            for (var i = 0; i < Environment.LightSources.Count; i++) {
                using (var lightGroup = BatchGroup.New(resultGroup, i)) {
                    var lightSource = Environment.LightSources[i];
                    var lightBounds = new Bounds(lightSource.Position - new Vector2(lightSource.RampEnd), lightSource.Position + new Vector2(lightSource.RampEnd));

                    ClearBatch.AddNew(lightGroup, 0, ClearStencil, clearStencil: 1);

                    using (var nb = NativeBatch.New(lightGroup, 1, Shadow, ShadowBatchSetup, lightSource)) {
                        SpatialCollection<LightObstruction>.Sector currentSector;
                        using (var e = Environment.Obstructions.GetSectorsFromBounds(lightBounds))
                        while (e.GetNext(out currentSector)) {
                            var cachedSector = GetCachedSector(frame, currentSector.Index);

                            nb.Add(new NativeDrawCall(
                                PrimitiveType.TriangleList, cachedSector.ObstructionVertexBuffer, 0, cachedSector.ObstructionIndexBuffer, 0, 0, cachedSector.VertexCount, 0, cachedSector.PrimitiveCount
                            ));
                        }
                    }

                    using (var pb = PrimitiveBatch<PointLightVertex>.New(lightGroup, 2, PointLight, IlluminationBatchSetup, lightSource))
                    using (var buffer = pb.CreateBuffer(4)) {
                        var writer = buffer.GetWriter(4);
                        PointLightVertex vertex;

                        vertex.LightCenter = lightSource.Position;
                        vertex.Color = lightSource.Color;
                        vertex.Ramp = new Vector2(lightSource.RampStart, lightSource.RampEnd);

                        vertex.Position = new Vector2(
                            lightSource.Position.X - lightSource.RampEnd, 
                            lightSource.Position.Y - lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        vertex.Position = new Vector2(
                            lightSource.Position.X + lightSource.RampEnd,
                            lightSource.Position.Y - lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        vertex.Position = new Vector2(
                            lightSource.Position.X + lightSource.RampEnd,
                            lightSource.Position.Y + lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        vertex.Position = new Vector2(
                            lightSource.Position.X - lightSource.RampEnd,
                            lightSource.Position.Y + lightSource.RampEnd
                        );
                        writer.Write(ref vertex);

                        pb.Add(writer.GetDrawCall(PrimitiveType.TriangleList, PointLightIndices, 0, PointLightIndices.Length));
                    }
                }
            }
        }

        public void RenderOutlines (Frame frame, int layer, bool showLights) {
            using (var group = BatchGroup.New(frame, layer)) {
                using (var gb = GeometryBatch<VertexPositionColor>.New(group, 0, DebugOutlines)) {
                    foreach (var lo in Environment.Obstructions)
                        gb.AddLine(lo.A, lo.B, Color.White);
                }

                if (showLights)
                for (var i = 0; i < Environment.LightSources.Count; i++) {
                    var lightSource = Environment.LightSources[i];

                    var cMax = new Color(1f, 1f, 1f, 1f);
                    var cMin = cMax * 0.25f;

                    using (var gb = GeometryBatch<VertexPositionColor>.New(group, i + 1, DebugOutlines)) {
                        gb.AddFilledRing(lightSource.Position, 0f, 2f, cMax, cMax);
                        gb.AddFilledRing(lightSource.Position, lightSource.RampStart - 1f, lightSource.RampStart + 1f, cMax, cMax);
                        gb.AddFilledRing(lightSource.Position, lightSource.RampEnd - 1f, lightSource.RampEnd + 1f, cMin, cMin);
                    }
                }
            }
        }
    }
}
