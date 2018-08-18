using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Squared.Game;
using Squared.Render;
using NuklearDotNet;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Evil;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Microsoft.Xna.Framework;

namespace TestGame {
    public unsafe class NuklearService : IDisposable {
        public class Device : NuklearDeviceTex<Texture2D> {
            public readonly NuklearService Service;

            private VertexPositionColorTexture[] Scratch;
            private VertexBuffer VertexBuffer;
            private IndexBuffer IndexBuffer;

            RenderCoordinator Coordinator {
                get {
                    return Service.Game.RenderCoordinator;
                }
            }

            GraphicsDevice GraphicsDevice {
                get {
                    return Service.Game.GraphicsDevice;
                }
            }

            internal Device (NuklearService service) {
                Service = service;
            }

            public override Texture2D CreateTexture (int W, int H, IntPtr Data) {
                // FIXME: Upload data
                Texture2D result;
                lock (Coordinator.CreateResourceLock)
                    result = new Texture2D(GraphicsDevice, W, H, false, SurfaceFormat.Color);
                lock (Coordinator.UseResourceLock) {
                    var pSurface = TextureUtils.GetSurfaceLevel(result, 0);
                    TextureUtils.SetData(result, pSurface, Data.ToPointer(), W, H, (uint)(W * 4), D3DFORMAT.A8B8G8R8);
                    Marshal.Release((IntPtr)pSurface);
                }
                return result;
            }

            public override void Render (NkHandle Userdata, Texture2D Texture, NkRect ClipRect, uint Offset, uint Count) {
                var group = Service.PendingGroup;
                var materials = Service.Game.Materials;
                using (var pb = NativeBatch.New(
                    group, 0, materials.Get(materials.ScreenSpaceTexturedGeometry, blendState: BlendState.NonPremultiplied), (dm, _) => {
                        dm.Device.Textures[0] = Texture;
                    }
                ))
                    pb.Add(new NativeDrawCall(PrimitiveType.TriangleList, VertexBuffer, 0, IndexBuffer, 0, 0, VertexBuffer.VertexCount, (int)Offset, (int)(Count)));
            }

            public override void SetBuffer (NkVertex[] Vertices, ushort[] Indices) {
                if ((VertexBuffer != null) && (VertexBuffer.VertexCount < Vertices.Length)) {
                    Coordinator.DisposeResource(VertexBuffer);
                    VertexBuffer = null;
                }
                if ((IndexBuffer != null) && (IndexBuffer.IndexCount < Indices.Length)) {
                    Coordinator.DisposeResource(IndexBuffer);
                    IndexBuffer = null;
                }

                if (VertexBuffer == null)
                    lock (Coordinator.CreateResourceLock)
                        VertexBuffer = new DynamicVertexBuffer(GraphicsDevice, typeof(VertexPositionColorTexture), Vertices.Length, BufferUsage.WriteOnly);
                if (IndexBuffer == null)
                    lock (Coordinator.CreateResourceLock)
                        IndexBuffer = new DynamicIndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, Indices.Length, BufferUsage.WriteOnly);

                // U G H
                if ((Scratch == null) || (Scratch.Length < Vertices.Length))
                    Scratch = new VertexPositionColorTexture[Vertices.Length];
                for (int i = 0; i < Vertices.Length; i++) {
                    var v = Vertices[i];
                    var c = v.Color;
                    Scratch[i] = new VertexPositionColorTexture {
                        Color = new Color(c.R, c.G, c.B, c.A),
                        Position = new Vector3(v.Position.X, v.Position.Y, 0),
                        TextureCoordinate = new Vector2(v.UV.X, v.UV.Y)
                    };
                }

                lock (Coordinator.UseResourceLock) {
                    VertexBuffer.SetData(Scratch);
                    IndexBuffer.SetData(Indices);
                }
            }
        }

        private IBatchContainer PendingGroup;
        private ImperativeRenderer PendingIR;

        private float _FontScale = 1.0f;
        private IGlyphSource _Font;
        private nk_query_font_glyph_f QueryFontGlyphF;
        private nk_text_width_f TextWidthF;

        public readonly TestGame Game;
        public Bounds Bounds;

        Device Instance;

        public NuklearService (TestGame game) {
            Game = game;
            QueryFontGlyphF = _QueryFontGlyphF;
            TextWidthF = _TextWidthF;
            Instance = new Device(this);
            NuklearAPI.Init(Instance);
        }

        public IGlyphSource Font {
            get {
                return _Font;
            }
            set {
                if (_Font == value)
                    return;
                _Font = value;
                SetNewFont(value);
            }
        }

        public float FontScale {
            get {
                return _FontScale;
            }
            set {
                if (_FontScale == value)
                    return;
                _FontScale = value;
                SetNewFont(_Font);
            }
        }

        private void _QueryFontGlyphF (NkHandle handle, float font_height, nk_user_font_glyph* glyph, uint codepoint, uint next_codepoint) {
            *glyph = default(nk_user_font_glyph);

            Glyph result;
            if (!_Font.GetGlyph((char)codepoint, out result))
                return;

            var texBounds = result.Texture.BoundsFromRectangle(ref result.BoundsInTexture);

            glyph->uv0 = new nk_vec2 { x = texBounds.TopLeft.X, y = texBounds.TopLeft.Y };
            glyph->uv1 = new nk_vec2 { x = texBounds.BottomRight.X, y = texBounds.BottomRight.Y };
            glyph->width = result.BoundsInTexture.Width * FontScale;
            glyph->height = result.BoundsInTexture.Height * FontScale;
            glyph->xadvance = result.Width * FontScale;
        }

        private float _TextWidthF (NkHandle handle, float h, byte* s, int len) {
            var textUtf8 = Encoding.UTF8.GetString(s, len);
            var layout = _Font.LayoutString(textUtf8, scale: FontScale);
            return layout.Size.X;
        }

        private void SetNewFont (IGlyphSource newFont) {
            var userFont = NuklearAPI.AllocUserFont();

            float estimatedHeight = 0;
            for (int i = 0; i < 255; i++) {
                Glyph glyph;
                if (newFont.GetGlyph((char)i, out glyph))
                    estimatedHeight = Math.Max(estimatedHeight, glyph.BoundsInTexture.Height);
            }
            // LineSpacing includes whitespace :(
            userFont->height = estimatedHeight * FontScale;

            userFont->queryfun_nkQueryFontGlyphF = Marshal.GetFunctionPointerForDelegate(QueryFontGlyphF);
            userFont->widthfun_nkTextWidthF = Marshal.GetFunctionPointerForDelegate(TextWidthF);
            Nuklear.nk_style_set_font(NuklearAPI.Ctx, userFont);
        }

        public void Update (float deltaTime) {
            // FIXME: Why?
        }

        private Color ConvertColor (NkColor c) {
            return new Color(c.R, c.G, c.B, c.A);
        }

        private Bounds ConvertBounds (float x, float y, float w, float h) {
            return Bounds.FromPositionAndSize(new Vector2(x, y), new Vector2(w, h));
        }

        private void RenderFilledRect (nk_command_rect_filled* c) {
            PendingIR.FillRectangle(
                ConvertBounds(c->x, c->y, c->w, c->h), 
                ConvertColor(c->color),
                blendState: BlendState.NonPremultiplied
            );
        }

        private void RenderText (nk_command_text* c) {
            var pTextUtf8 = &c->stringFirstByte;
            var text = Encoding.UTF8.GetString(pTextUtf8, c->length);
            PendingIR.DrawString(
                _Font, text, new Vector2(c->x, c->y), color: ConvertColor(c->foreground), layer: 2, scale: FontScale, blendState: BlendState.AlphaBlend
            );
        }

        private void HighLevelRenderCommand (nk_command* c) {
            switch (c->ctype) {
                case nk_command_type.NK_COMMAND_RECT_FILLED:
                    RenderFilledRect((nk_command_rect_filled*)c);
                    break;
                case nk_command_type.NK_COMMAND_TEXT:
                    RenderText((nk_command_text*)c);
                    break;
                default:
                    break;
            }
        }

        public void Render (float deltaTime, IBatchContainer container, int layer) {
            NuklearAPI.SetDeltaTime(deltaTime);
            // FIXME: Gross

            using (var group = BatchGroup.New(container, layer)) {
                PendingGroup = group;
                PendingIR = new ImperativeRenderer(group, Game.Materials, 0);
                NuklearAPI.Frame(
                    () => {
                        NuklearAPI.Window("Test", 4, 4, 320, 240, NkPanelFlags.Border | NkPanelFlags.Title, () => {; });
                    }, 
                    HighLevelRenderCommand
                );
            }
        }

        public void Dispose () {
            // FIXME
        }
    }
}