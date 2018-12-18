﻿using System;
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

namespace Framework {
    public interface INuklearHost {
        RenderCoordinator RenderCoordinator { get; }
        DefaultMaterialSet Materials { get; }
        Material TextMaterial { get; }
    }

    public unsafe class NuklearService : IDisposable {
        public struct Generic : IDisposable {
            public nk_context* ctx;
            public bool Visible;

            public void Dispose () {
                Nuklear.nk_end(ctx);
            }
        }

        public struct GroupScrolled : IDisposable {
            public nk_context* ctx;
            public bool Visible;

            public void Dispose () {
                if (Visible)
                    Nuklear.nk_group_scrolled_end(ctx);
            }
        }

        public struct Tree : IDisposable {
            public nk_context* ctx;
            public bool Visible;

            public void Dispose () {
                if (Visible)
                    Nuklear.nk_tree_pop(ctx);
            }
        }

        public nk_context* Context;

        private IBatchContainer PendingGroup;
        private ImperativeRenderer PendingIR;
        private int NextTextLayer;

        private float _FontScale = 1.0f;
        private IGlyphSource _Font;
        private nk_query_font_glyph_f QueryFontGlyphF;
        private nk_text_width_f TextWidthF;

        public Action Scene = null;
        public UnorderedList<Func<bool>> Modals = new UnorderedList<Func<bool>>();

        public readonly INuklearHost Game;

        private readonly Dictionary<string, float> TextWidthCache = new Dictionary<string, float>(StringComparer.Ordinal);

        public NuklearService (INuklearHost game) {
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
            if ((s == null) || (len == 0))
                return 0;

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
            var color = ConvertColor(c->color);
            var colorBright = new Color(color.R + 20, color.G + 20, color.B + 20, color.A);

            switch (c->header.mtype) {
                case nk_meta_type.NK_META_TREE_HEADER:
                    PendingIR.GradientFillRectangle(
                        ConvertBounds(c->x, c->y, c->w, c->h), 
                        color, colorBright, color, colorBright
                    );
                    break;
                default:
                    PendingIR.FillRectangle(
                        ConvertBounds(c->x, c->y, c->w, c->h), 
                        color
                    );
                    break;
            }

            TextAdvancePending = false;
        }

        private void RenderCommand (nk_command_text* c) {
            var pTextUtf8 = &c->stringFirstByte;
            int charsDecoded;
            using (var charBuffer = BufferPool<char>.Allocate(c->length + 1)) {
                fixed (char* pChars = charBuffer.Data)
                    charsDecoded = Encoding.UTF8.GetChars(pTextUtf8, c->length, pChars, charBuffer.Data.Length);
                var str = new AbstractString(new ArraySegment<char>(charBuffer.Data, 0, charsDecoded));
                var color = ConvertColor(c->foreground);

                if (c->header.mtype == nk_meta_type.NK_META_PROPERTY_LABEL)
                    color *= 0.66f;

                using (var layoutBuffer = BufferPool<BitmapDrawCall>.Allocate(c->length + 64)) {
                    var layout = _Font.LayoutString(
                        str, position: new Vector2(c->x, c->y),
                        color: color,
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
            var bounds = ConvertBounds(c->x, c->y, c->w - 1, c->h - 1);
            var radius = bounds.Size / 2f;
            var color = ConvertColor(c->color);
            /*
            var softEdge = Vector2.One * 1f;
            PendingIR.FillRing(bounds.Center, Vector2.Zero, radius - Vector2.One, color, color, quality: 2);
            PendingIR.FillRing(bounds.Center, radius - (Vector2.One * 1.4f), radius + softEdge, color, Color.Transparent, quality: 2);
            */
            PendingIR.Ellipse(bounds.Center, radius, color);
        }
        
        private void RenderCommand (nk_command_line* c) {
            var gb = PendingIR.GetGeometryBatch(null, null, null);
            var color = ConvertColor(c->color);
            var v1 = new Vector2(c->begin.x, c->begin.y);
            var v2 = new Vector2(c->end.x, c->end.y);
            gb.AddLine(v1, v2, color);
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

        private void RenderCommand (nk_command_rect_multi_color* c) {
            if (TextAdvancePending)
                PendingIR.Layer += 1;
            PendingIR.GradientFillRectangle(
                ConvertBounds(c->x, c->y, c->w, c->h),
                ConvertColor(c->left),
                ConvertColor(c->top),
                ConvertColor(c->bottom),
                ConvertColor(c->right)
            );
            TextAdvancePending = false;
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
                case nk_command_type.NK_COMMAND_LINE:
                    RenderCommand((nk_command_line*)c);
                    break;
                case nk_command_type.NK_COMMAND_TRIANGLE_FILLED:
                    RenderCommand((nk_command_triangle_filled*)c);
                    break;
                case nk_command_type.NK_COMMAND_RECT_MULTI_COLOR:
                    RenderCommand((nk_command_rect_multi_color*)c);
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

                using (var e = Modals.GetEnumerator()) {
                    while (e.MoveNext()) {
                        if (!e.Current())
                            e.RemoveCurrent();
                    }
                }

                NuklearAPI.Render(Context, HighLevelRenderCommand);
            }
        }

        private static readonly Dictionary<Keys, NkKeys> KeyMap = new Dictionary<Keys, NkKeys> {
            { Keys.LeftControl, NkKeys.Ctrl },
            { Keys.RightControl, NkKeys.Ctrl },
            { Keys.LeftShift, NkKeys.Shift },
            { Keys.RightShift, NkKeys.Shift },
            { Keys.Back, NkKeys.Backspace },
            { Keys.Delete, NkKeys.Del },
            { Keys.Left, NkKeys.Left },
            { Keys.Right, NkKeys.Right },
            { Keys.Up, NkKeys.Up },
            { Keys.Down, NkKeys.Down },
            { Keys.Enter, NkKeys.Enter },
            { Keys.Tab, NkKeys.Tab },
            { Keys.Home, NkKeys.LineStart },
            { Keys.End, NkKeys.LineEnd }
        };

        public unsafe void UpdateInput (
            MouseState previousMouseState, MouseState mouseState,
            KeyboardState previousKeyboardState, KeyboardState keyboardState,
            bool processMousewheel, IEnumerable<char> keystrokes = null
        ) {
            NString.GC();

            var ctx = Context;
            Nuklear.nk_input_begin(ctx);
            if ((mouseState.X != previousMouseState.X) || (mouseState.Y != previousMouseState.Y))
                Nuklear.nk_input_motion(ctx, mouseState.X, mouseState.Y);
            if (mouseState.LeftButton != previousMouseState.LeftButton)
                Nuklear.nk_input_button(ctx, nk_buttons.NK_BUTTON_LEFT, mouseState.X, mouseState.Y, mouseState.LeftButton == ButtonState.Pressed ? 1 : 0);
            var scrollDelta = (mouseState.ScrollWheelValue - previousMouseState.ScrollWheelValue) / 106f;
            if ((scrollDelta != 0) && processMousewheel)
                Nuklear.nk_input_scroll(ctx, new nk_vec2(0, scrollDelta));

            var previousKeys = new HashSet<Keys>(previousKeyboardState.GetPressedKeys());
            var currentKeys = new HashSet<Keys>(keyboardState.GetPressedKeys());

            foreach (var key in previousKeys) {
                if (currentKeys.Contains(key))
                    continue;
                NkKeys mapped;
                if (!KeyMap.TryGetValue(key, out mapped))
                    continue;
                Nuklear.nk_input_key(ctx, mapped, 0);
            }
            foreach (var key in currentKeys) {
                if (previousKeys.Contains(key))
                    continue;
                NkKeys mapped;
                if (!KeyMap.TryGetValue(key, out mapped))
                    continue;
                Nuklear.nk_input_key(ctx, mapped, 1);
            }

            if (keystrokes != null) {
                foreach (var ch in keystrokes)
                    Nuklear.nk_input_char(ctx, (byte)ch);
            }

            Nuklear.nk_input_end(ctx);
        }

        public Generic Window (string name, Bounds bounds, NkPanelFlags flags) {
            var visible = Nuklear.nk_begin(
                Context, name, new NkRect(
                    bounds.TopLeft.X, bounds.TopLeft.Y,
                    bounds.Size.X, bounds.Size.Y
                ), (uint)flags
            ) != 0;

            return new Generic {
                ctx = Context,
                Visible = visible
            };
        }

        public Tree CollapsingGroup (string caption, string name, bool defaultOpen = true, int hash = 0) {
            using (var tCaption = new NString(caption))
            using (var tName = new NString(name)) {
                var result = Nuklear.nk_tree_push_hashed(
                    Context, nk_tree_type.NK_TREE_TAB, tCaption.pText,
                    defaultOpen ? nk_collapse_states.NK_MAXIMIZED : nk_collapse_states.NK_MINIMIZED, tName.pText, tName.Length, hash
                );
                return new Tree {
                    ctx = Context,
                    Visible = (result != 0)
                };
            }
        }

        public bool Property (string name, ref int value, int min, int max, int step, int inc_per_pixel) {
            using (var sName = new NString(name)) {
                var newValue = Nuklear.nk_propertyi(Context, sName.pText, min, value, max, step, inc_per_pixel);
                var result = newValue != value;
                value = newValue;
                return result;
            }
        } 

        public bool Property (string name, ref float value, float min, float max, float step, float inc_per_pixel) {
            using (var sName = new NString(name)) {
                var newValue = Nuklear.nk_propertyf(Context, sName.pText, min, value, max, step, inc_per_pixel);
                var result = newValue != value;
                value = newValue;
                return result;
            }
        } 

        public bool SelectableText (string name, bool state) {
            var flags = (uint)NkTextAlignment.NK_TEXT_LEFT;
            int selected = state ? 1 : 0;
            using (var s = new NString(name))
                Nuklear.nk_selectable_text(Context, s.pText, s.Length, flags, ref selected);
            return selected != 0;
        }

        public GroupScrolled ScrollingGroup (float heightPx, string name, ref uint scrollX, ref uint scrollY) {
            using (var tName = new NString(name)) {
                uint flags = 0;
                Nuklear.nk_layout_row(Context, nk_layout_format.NK_DYNAMIC, heightPx, 1, new[] { 1.0f });
                var result = Nuklear.nk_group_scrolled_offset_begin(Context, ref scrollX, ref scrollY, tName.pText, flags);

                return new GroupScrolled {
                    ctx = Context,
                    Visible = result != 0
                };
            }
        }

        public bool Button (string text) {
            return Nuklear.nk_button_label(Context, text) != 0;
        }

        public bool ComboBox (ref int selectedIndex, Func<int, string> getter, int count) {
            var rect = Nuklear.nk_layout_space_bounds(Context);
            var strings = new List<NString>();
            nk_item_getter_fun wrappedGetter = (user, i, idk) => {
                var text = getter(i);
                var str = new NString(text);
                *idk = str.pText;
                strings.Add(str);
            };
            var oldIndex = selectedIndex;
            Nuklear.nk_combobox_callback(
                Context, wrappedGetter, IntPtr.Zero, 
                ref selectedIndex, count, (int)_Font.LineSpacing, new NuklearDotNet.nk_vec2(rect.W - 32, 512)
            );
            foreach (var s in strings)
                s.Dispose();
            return (oldIndex != selectedIndex);
        }

        public void Dispose () {
            // FIXME
        }
    }

    public unsafe struct NString : IDisposable {
        private static readonly List<IntPtr> ToFree = new List<IntPtr>();

        private static byte[] NullString;
        private static GCHandle hNullString;
        private static byte* pNullString;

        private bool OwnsPointer;
        public byte* pText;
        public int Length;

        static NString () {
            NullString = new byte[2];
            hNullString = GCHandle.Alloc(NullString, GCHandleType.Pinned);
            pNullString = (byte*)hNullString.AddrOfPinnedObject().ToPointer();
        }

        public NString (string text) {
            if (string.IsNullOrEmpty(text)) {
                pText = pNullString;
                Length = 1;
                OwnsPointer = false;
                return;
            }

            OwnsPointer = true;
            var encoder = Encoding.UTF8.GetEncoder();
            fixed (char* pChars = text) {
                Length = encoder.GetByteCount(pChars, text.Length, true);
                pText = (byte*)NuklearAPI.Malloc((IntPtr)(Length + 2)).ToPointer();
                int temp;
                bool temp2;
                encoder.Convert(pChars, text.Length, pText, Length, true, out temp, out temp, out temp2);
                pText[Length] = 0;
            }
        }

        public static void GC () {
            lock (ToFree) {
                foreach (var ptr in ToFree)
                    NuklearAPI.StdFree(ptr);
                ToFree.Clear();
            }
        }

        public void Dispose () {
            if (OwnsPointer)
            lock (ToFree)
                ToFree.Add((IntPtr)pText);

            pText = null;
            Length = 0;
        }
    }
}