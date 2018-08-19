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
using Microsoft.Xna.Framework.Input;

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

        private readonly Dictionary<string, float> TextWidthCache = new Dictionary<string, float>(StringComparer.Ordinal);

        public NuklearService (TestGame game) {
            Game = game;
            QueryFontGlyphF = _QueryFontGlyphF;
            TextWidthF = _TextWidthF;
            Context = NuklearAPI.Init();
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

        private bool TextAdvancePending = false;

        private void _QueryFontGlyphF (NkHandle handle, float font_height, nk_user_font_glyph* glyph, uint codepoint, uint next_codepoint) {
            *glyph = default(nk_user_font_glyph);

            Glyph result;
            if (!_Font.GetGlyph((char)codepoint, out result))
                return;

            var texBounds = result.Texture.BoundsFromRectangle(ref result.BoundsInTexture);

            glyph->uv0 = (nk_vec2)texBounds.TopLeft;
            glyph->uv1 = (nk_vec2)texBounds.BottomRight;
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
            return new Color(c.R * c.A / 255, c.G * c.A / 255, c.B * c.A / 255, c.A);
        }

        private Bounds ConvertBounds (float x, float y, float w, float h) {
            return Bounds.FromPositionAndSize(new Vector2(x, y), new Vector2(w, h));
        }

        private void RenderCommand (nk_command_rect* c) {
            PendingIR.OutlineRectangle(
                ConvertBounds(c->x, c->y, c->w - 1, c->h - 1), 
                ConvertColor(c->color)
            );
        }

        private void RenderCommand (nk_command_rect_filled* c) {
            if (TextAdvancePending)
                PendingIR.Layer += 1;
            PendingIR.FillRectangle(
                ConvertBounds(c->x, c->y, c->w, c->h), 
                ConvertColor(c->color)
            );
            TextAdvancePending = false;
        }

        private void RenderCommand (nk_command_text* c) {
            var pTextUtf8 = &c->stringFirstByte;
            int charsDecoded;
            using (var charBuffer = BufferPool<char>.Allocate(c->length + 1)) {
                fixed (char* pChars = charBuffer.Data)
                    charsDecoded = Encoding.UTF8.GetChars(pTextUtf8, c->length, pChars, charBuffer.Data.Length);
                var str = new AbstractString(new ArraySegment<char>(charBuffer.Data, 0, charsDecoded));
                using (var layoutBuffer = BufferPool<BitmapDrawCall>.Allocate(c->length + 64)) {
                    var layout = _Font.LayoutString(
                        str, position: new Vector2(c->x, c->y),
                        color: ConvertColor(c->foreground),
                        scale: FontScale, buffer: new ArraySegment<BitmapDrawCall>(layoutBuffer.Data)
                    );
                    PendingIR.DrawMultiple(
                        layout.DrawCalls, material: Game.TextMaterial
                    );
                }
                TextAdvancePending = true;
            }
        }

        private void RenderCommand (nk_command_scissor* c) {
            var rect = new Rectangle(c->x, c->y, c->w, c->h);
            PendingIR.Layer += 1;
            PendingIR.SetScissor(rect);
            PendingIR.Layer += 1;
            TextAdvancePending = false;
        }

        private void RenderCommand (nk_command_circle_filled* c) {
            var bounds = ConvertBounds(c->x, c->y, c->w, c->h);
            var radius = bounds.Size / 2f;
            var color = ConvertColor(c->color);
            var softEdge = Vector2.One * 2f;
            PendingIR.FillRing(bounds.Center, Vector2.Zero, radius - Vector2.One, color, color);
            PendingIR.FillRing(bounds.Center, radius - (Vector2.One * 1.4f), radius + softEdge, color, Color.Transparent);
        }

        private void RenderCommand (nk_command_triangle_filled* c) {
            var gb = PendingIR.GetGeometryBatch(null, null, null);
            var color = ConvertColor(c->color);
            var v1 = new Vector2(c->a.x, c->a.y);
            var v2 = new Vector2(c->b.x, c->b.y);
            var v3 = new Vector2(c->c.x, c->c.y);
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
            // FIXME: Gross

            using (var group = BatchGroup.New(container, layer, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
            })) {
                PendingGroup = group;
                PendingIR = new ImperativeRenderer(group, Game.Materials, 0, autoIncrementSortKey: true, worldSpace: false, blendState: BlendState.AlphaBlend);

                Scene();
                NuklearAPI.Render(Context, HighLevelRenderCommand);
            }
        }

        public unsafe void UpdateInput (
            MouseState previousMouseState, MouseState mouseState,
            KeyboardState previousKeyboardState, KeyboardState keyboardState,
            bool processMousewheel
        ) {
            var ctx = Context;
            Nuklear.nk_input_begin(ctx);
            if ((mouseState.X != previousMouseState.X) || (mouseState.Y != previousMouseState.Y))
                Nuklear.nk_input_motion(ctx, mouseState.X, mouseState.Y);
            if (mouseState.LeftButton != previousMouseState.LeftButton)
                Nuklear.nk_input_button(ctx, nk_buttons.NK_BUTTON_LEFT, mouseState.X, mouseState.Y, mouseState.LeftButton == ButtonState.Pressed ? 1 : 0);
            var scrollDelta = (mouseState.ScrollWheelValue - previousMouseState.ScrollWheelValue) / 106f;
            if ((scrollDelta != 0) && processMousewheel)
                Nuklear.nk_input_scroll(ctx, new nk_vec2(0, scrollDelta));
            Nuklear.nk_input_end(ctx);
        }

        public void Dispose () {
            // FIXME
        }
    }
}