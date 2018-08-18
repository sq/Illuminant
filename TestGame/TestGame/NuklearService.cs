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
using Squared.Util;

namespace TestGame {
    public unsafe class NuklearService : IDisposable {
        public nk_context* Context;

        private IBatchContainer PendingGroup;
        private ImperativeRenderer PendingIR;
        private int NextTextLayer;

        private float _FontScale = 1.0f;
        private IGlyphSource _Font;
        private nk_query_font_glyph_f QueryFontGlyphF;
        private nk_text_width_f TextWidthF;

        public Action Scene = null;

        public readonly TestGame Game;
        public Bounds Bounds;

        private readonly Dictionary<string, float> TextWidthCache = new Dictionary<string, float>(StringComparer.Ordinal);

        public NuklearService (TestGame game) {
            Game = game;
            QueryFontGlyphF = _QueryFontGlyphF;
            TextWidthF = _TextWidthF;
            Context = (nk_context*)NuklearAPI.Malloc((IntPtr)sizeof(nk_context));
            Nuklear.nk_init(Context, NuklearAPI.MakeAllocator(), null);
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
            float result;
            if (!TextWidthCache.TryGetValue(textUtf8, out result))
                TextWidthCache[textUtf8] = result = _Font.LayoutString(textUtf8, scale: FontScale).Size.X;
            return result;
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
            userFont->height = (estimatedHeight - 3) * FontScale;

            userFont->queryfun_nkQueryFontGlyphF = Marshal.GetFunctionPointerForDelegate(QueryFontGlyphF);
            userFont->widthfun_nkTextWidthF = Marshal.GetFunctionPointerForDelegate(TextWidthF);
            Nuklear.nk_style_set_font(Context, userFont);
        }

        private Color ConvertColor (NkColor c) {
            return new Color(c.R, c.G, c.B, c.A);
        }

        private Bounds ConvertBounds (float x, float y, float w, float h) {
            return Bounds.FromPositionAndSize(new Vector2(x, y), new Vector2(w, h));
        }

        private void RenderCommand (nk_command_rect* c) {
            PendingIR.OutlineRectangle(
                ConvertBounds(c->x, c->y, c->w - 1, c->h - 1), 
                ConvertColor(c->color),
                blendState: BlendState.NonPremultiplied
            );
        }

        private void RenderCommand (nk_command_rect_filled* c) {
            PendingIR.FillRectangle(
                ConvertBounds(c->x, c->y, c->w, c->h), 
                ConvertColor(c->color),
                blendState: BlendState.NonPremultiplied
            );
        }

        private void RenderCommand (nk_command_text* c) {
            var pTextUtf8 = &c->stringFirstByte;
            int charsDecoded;
            using (var charBuffer = BufferPool<char>.Allocate(c->length + 1)) {
                fixed (char* pChars = charBuffer.Data)
                    charsDecoded = Encoding.UTF8.GetChars(pTextUtf8, c->length, pChars, charBuffer.Data.Length);
                var str = new AbstractString(new ArraySegment<char>(charBuffer.Data, 0, charsDecoded));
                var layout = _Font.LayoutString(
                    str, position: new Vector2(c->x, c->y),
                    color: ConvertColor(c->foreground),
                    scale: FontScale
                );
                PendingIR.DrawMultiple(
                    layout.DrawCalls, material: Game.TextMaterial
                );
            }
        }

        private void RenderCommand (nk_command_scissor* c) {
            var rect = new Rectangle(c->x, c->y, c->w, c->h);
            if (rect.X < 0)
                rect.X = 0;
            if (rect.Y < 0)
                rect.Y = 0;
            var x2 = rect.Width + rect.X;
            var y2 = rect.Height + rect.Y;
            if (x2 > Game.Graphics.PreferredBackBufferWidth)
                rect.Width = Game.Graphics.PreferredBackBufferWidth - rect.X;
            if (y2 > Game.Graphics.PreferredBackBufferHeight)
                rect.Height = Game.Graphics.PreferredBackBufferHeight - rect.Y;
            if (rect.Width < 0)
                rect.Width = 0;
            if (rect.Height < 0)
                rect.Height = 0;
            PendingIR.SetScissor(rect);
        }

        private void RenderCommand (nk_command_circle_filled* c) {
            var gb = PendingIR.GetGeometryBatch(null, false, BlendState.AlphaBlend);
            var bounds = ConvertBounds(c->x, c->y, c->w, c->h);
            var radius = bounds.Size / 2f;
            var color = ConvertColor(c->color);
            var softEdge = Vector2.One * 2f;
            gb.AddFilledRing(bounds.Center, Vector2.Zero, radius - Vector2.One, color, color);
            gb.AddFilledRing(bounds.Center, radius - (Vector2.One * 1.4f), radius + softEdge, color, Color.Transparent);
        }

        private void RenderCommand (nk_command_triangle_filled* c) {
            var gb = PendingIR.GetGeometryBatch(null, false, BlendState.AlphaBlend);
            var color = ConvertColor(c->color);
            var v1 = new Vector2(c->a.x, c->a.y);
            var v2 = new Vector2(c->a.x, c->a.y);
            var v3 = new Vector2(c->a.x, c->a.y);
            // FIXME: Fill the triangle
            // FIXME: Why are these lines invisible?
            gb.AddLine(v1, v2, color);
            gb.AddLine(v2, v3, color);
            gb.AddLine(v3, v1, color);
        }

        private HashSet<string> WarnedCommands = new HashSet<string>();

        private void HighLevelRenderCommand (nk_command* c) {
            switch (c->ctype) {
                case nk_command_type.NK_COMMAND_RECT:
                    RenderCommand((nk_command_rect*)c);
                    break;
                case nk_command_type.NK_COMMAND_RECT_FILLED:
                    RenderCommand((nk_command_rect_filled*)c);
                    break;
                case nk_command_type.NK_COMMAND_TEXT:
                    RenderCommand((nk_command_text*)c);
                    break;
                case nk_command_type.NK_COMMAND_SCISSOR:
                    RenderCommand((nk_command_scissor*)c);
                    break;
                case nk_command_type.NK_COMMAND_CIRCLE_FILLED:
                    RenderCommand((nk_command_circle_filled*)c);
                    break;
                case nk_command_type.NK_COMMAND_TRIANGLE_FILLED:
                    RenderCommand((nk_command_triangle_filled*)c);
                    break;
                default:
                    var name = c->ctype.ToString();
                    if (!WarnedCommands.Contains(name)) {
                        Console.WriteLine("Not implemented: {0}", name);
                        WarnedCommands.Add(name);
                    }
                    break;
            }
        }

        public void Render (float deltaTime, IBatchContainer container, int layer) {
            if (Scene == null)
                return;
            NuklearAPI.SetDeltaTime(deltaTime);
            // FIXME: Gross

            using (var group = BatchGroup.New(container, layer, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
            })) {
                PendingGroup = group;
                PendingIR = new ImperativeRenderer(group, Game.Materials, 0, autoIncrementLayer: true);

                Scene();
                Nuklear.nk_foreach(Context, HighLevelRenderCommand);
            }

            Nuklear.nk_clear(Context);
        }

        public void Dispose () {
            // FIXME
        }
    }
}