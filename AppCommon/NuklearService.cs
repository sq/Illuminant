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
using System.Threading;
using System.Collections;

namespace Framework {
    public interface INuklearHost {
        RenderCoordinator RenderCoordinator { get; }
        DefaultMaterialSet Materials { get; }
        Material TextMaterial { get; }
    }

    public delegate void SceneDelegate ();

    public struct RowLayout : IEnumerable<float> {
        public struct ColumnSpec {
            public bool IsFractional;
            public float Value;
        }

        private int Count;
        private ColumnSpec[] Buffer;

        private void Add (ref ColumnSpec value) {
            if ((Buffer != null) && (Buffer.Length <= Count))
                Buffer = null;
            if (Buffer == null)
                Buffer = new ColumnSpec[Math.Max(Count + 4, 8)];

            Buffer[Count] = value;
            Count++;
        }

        public void Add (float value, bool fractional = true) {
            var cs = new ColumnSpec {
                IsFractional = fractional,
                Value = value
            };
            Add(ref cs);
        }

        public unsafe void Apply (NuklearService service, float rowHeight) {
            var rect = Nuklear.nk_window_get_bounds(service.Context);
            var availableWidth = rect.W;

            for (int i = 0; i < Count; i++)
                if (!Buffer[i].IsFractional)
                    availableWidth -= Buffer[i].Value;

            var ratios = new float[Count + 1];
            for (int i = 0; i < Count; i++) {
                if (Buffer[i].IsFractional)
                    ratios[i] = (Buffer[i].Value * availableWidth) / rect.W;
                else
                    ratios[i] = Buffer[i].Value / rect.W;
            }

            var p = service.PinForOneFrame(ratios);
            Nuklear.nk_layout_row(service.Context, nk_layout_format.NK_DYNAMIC, rowHeight, Count, (float*)p);
        }

        IEnumerator<float> IEnumerable<float>.GetEnumerator () {
            for (int i = 0; i < Count; i++)
                yield return Buffer[i].Value;
        }

        IEnumerator IEnumerable.GetEnumerator () {
            for (int i = 0; i < Count; i++)
                yield return Buffer[i].Value;
        }
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

        internal IBatchContainer PendingGroup;
        internal ImperativeRenderer PendingRenderer;
        private int NextTextLayer;

        public float VerticalPadding = 0;

        private float _FontScale = 1.0f;
        private IGlyphSource _Font;
        private nk_query_font_glyph_f QueryFontGlyphF;
        private nk_text_width_f TextWidthF;

        private struct TextCacheKey {
            public int Length;
            public uint Hash;
            public byte FirstByte, LastByte;
        }
        private class TextCacheKeyComparer : IEqualityComparer<TextCacheKey> {
            public bool Equals (TextCacheKey x, TextCacheKey y) {
                return (x.Length == y.Length) &&
                    (x.Hash == y.Hash) &&
                    (x.FirstByte == y.FirstByte) &&
                    (x.LastByte == y.LastByte);
            }

            public int GetHashCode (TextCacheKey obj) {
                unchecked {
                    return (int)obj.Hash;
                }
            }
        }
        private Dictionary<char, float> CharWidthCache = new Dictionary<char, float>();
        private Dictionary<TextCacheKey, float> TextWidthCache = new Dictionary<TextCacheKey, float>(new TextCacheKeyComparer());

        public SceneDelegate Scene = null;
        public UnorderedList<Func<bool>> Modals = new UnorderedList<Func<bool>>();

        public Bounds? SceneBounds = null;

        public readonly INuklearHost Game;

        public NuklearService (INuklearHost game) {
            Game = game;
            QueryFontGlyphF = _QueryFontGlyphF;
            TextWidthF = _TextWidthF;
            Context = NuklearAPI.Init();

            InitStyle();
        }

        private unsafe void InitStyle () {
            var s = &Context->style;
            s->selectable.normal.data.color = 
                // s.selectable.normal_active.data.color =
                s->edit.normal.data.color =
                s->property.normal.data.color = default(NkColor);
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

        public void InvalidateFontCache () {
            CharWidthCache.Clear();
            TextWidthCache.Clear();
        }

        private bool TextAdvancePending = false;

        private void _QueryFontGlyphF (NkHandle handle, float font_height, nk_user_font_glyph* glyph, uint codepoint, uint next_codepoint) {
            *glyph = default(nk_user_font_glyph);

            Glyph result;
            if (!_Font.GetGlyph((char)codepoint, out result))
                return;

            var texBounds = result.BoundsInTexture;

            glyph->uv0 = new nk_vec2(texBounds.TopLeft.X, texBounds.TopLeft.Y);
            glyph->uv1 = new nk_vec2(texBounds.BottomRight.X, texBounds.BottomRight.Y);
            glyph->width = result.RectInTexture.Width * FontScale;
            glyph->height = (result.RectInTexture.Height * FontScale) + VerticalPadding;
            glyph->xadvance = result.Width * FontScale;
        }

        private TextCacheKey KeyForText (byte* s, int len) {
            if (len == 0)
                return default(TextCacheKey);

            var lastIdx = Math.Max(len - 1, 0);
            var hash = Nuklear.nk_murmur_hash((IntPtr)s, len, 0);
            return new TextCacheKey {
                FirstByte = s[0],
                LastByte = s[lastIdx],
                Hash = hash,
                Length = len
            };
        }

        private unsafe float _TextWidthF (NkHandle handle, float h, byte* s, int len) {
            float result;

            if ((s == null) || (len == 0))
                return 0;

            if (len == 1) {
                char* temp = stackalloc char[4];
                var cnt = Encoding.UTF8.GetChars(s, len, temp, 4);
                var ch = temp[0];

                if (!CharWidthCache.TryGetValue(ch, out result)) {
                    result = 0;
                    if (ch >= 32) {
                        Glyph glyph;
                        if (Font.GetGlyph(ch, out glyph)) {
                            result = glyph.LeftSideBearing + 
                                glyph.RightSideBearing + 
                                glyph.Width + glyph.CharacterSpacing;
                        }
                    }
                    CharWidthCache[ch] = result;
                }

                return result;
            }

            if ((len == 1) && (s[0] == 0))
                return 0;

            var key = KeyForText(s, len);
            if (!TextWidthCache.TryGetValue(key, out result)) {
                using (var buf = BufferPool<char>.Allocate(len + 1))
                using (var layoutBuf = BufferPool<BitmapDrawCall>.Allocate(len + 1)) {
                    int cnt;
                    fixed (char* pResult = buf.Data)
                        cnt = Encoding.UTF8.GetChars(s, len, pResult, buf.Data.Length);
                    var astr = new AbstractString(new ArraySegment<char>(buf.Data, 0, cnt));
                    result = _Font.LayoutString(astr, layoutBuf, scale: FontScale).Size.X;
                    TextWidthCache[key] = result;
                }
            }

            return result;
        }

        private void SetNewFont (IGlyphSource newFont) {
            InvalidateFontCache();

            var userFont = NuklearAPI.AllocUserFont();

            float estimatedHeight = 0;
            for (int i = 0; i < 255; i++) {
                Glyph glyph;
                if (newFont.GetGlyph((char)i, out glyph))
                    estimatedHeight = Math.Max(estimatedHeight, glyph.RectInTexture.Height / newFont.DPIScaleFactor);
            }
            // LineSpacing includes whitespace :(
            userFont->height = (estimatedHeight + VerticalPadding) * FontScale;

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
            var color = ConvertColor(c->color);
            if (color.A <= 0)
                return;

            PendingRenderer.OutlineRectangle(
                ConvertBounds(c->x, c->y, c->w - 1, c->h - 1), 
                color
            );
        }

        private void RenderCommand (nk_command_rect_filled* c) {
            var color = ConvertColor(c->color);
            var colorBright = new Color(color.R + 20, color.G + 20, color.B + 20, color.A);

            if (color.A <= 0)
                return;

            if (TextAdvancePending)
                PendingRenderer.Layer += 1;

            CurrentPaintIndex++;
            // color = new Color((CurrentPaintIndex % 16) * 8, (CurrentPaintIndex / 16) * 8, (CurrentPaintIndex / 128) * 8, 255);

            switch (c->header.mtype) {
                case nk_meta_type.NK_META_TREE_HEADER:
                    PendingRenderer.GradientFillRectangle(
                        ConvertBounds(c->x, c->y, c->w, c->h), 
                        color, colorBright, color, colorBright
                    );
                    break;
                default:
                    PendingRenderer.FillRectangle(
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
                        str, position: new Vector2(c->x, c->y - 1),
                        color: color,
                        scale: FontScale, buffer: new ArraySegment<BitmapDrawCall>(layoutBuffer.Data)
                    );
                    PendingRenderer.DrawMultiple(
                        layout.DrawCalls, material: Game.TextMaterial
                    );
                }
                TextAdvancePending = true;
            }
        }

        private void RenderCommand (nk_command_scissor* c) {
            var rect = new Rectangle(c->x, c->y, c->w, c->h);
            PendingRenderer.Layer += 1;
            PendingRenderer.SetScissor(rect);
            PendingRenderer.Layer += 1;
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
            PendingRenderer.RasterizeEllipse(bounds.Center, radius, color);
        }
        
        private void RenderCommand (nk_command_line* c) {
            var gb = PendingRenderer.GetGeometryBatch(null, null, null);
            var color = ConvertColor(c->color);
            var v1 = new Vector2(c->begin.x, c->begin.y);
            var v2 = new Vector2(c->end.x, c->end.y);
            gb.AddLine(v1, v2, color);
        }

        private void RenderCommand (nk_command_triangle_filled* c) {
            var color = ConvertColor(c->color);
            var v1 = new Vector2(c->a.x, c->a.y);
            var v2 = new Vector2(c->b.x, c->b.y);
            var v3 = new Vector2(c->c.x, c->c.y);
            /*
            var gb = PendingRenderer.GetGeometryBatch(null, null, null);
            // FIXME: Fill the triangle
            // FIXME: Why are these lines invisible?
            gb.AddLine(v1, v2, color);
            gb.AddLine(v2, v3, color);
            gb.AddLine(v3, v1, color);
            */
            PendingRenderer.RasterizeTriangle(
                v1, v2, v3, 0, color, color, blendState: BlendState.AlphaBlend,
                // FIXME: The layering for this geometry is complete garbage so just draw them top most
                layer: 9999
            );
        }

        private void RenderCommand (nk_command_rect_multi_color* c) {
            if (TextAdvancePending)
                PendingRenderer.Layer += 1;
            PendingRenderer.GradientFillRectangle(
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

        int CurrentPaintIndex = 0;

        public void Render (float deltaTime, IBatchContainer container, int layer) {
            if (Scene == null)
                return;
            // FIXME: Gross

            using (var group = BatchGroup.New(container, layer, (dm, _) => {
                dm.Device.RasterizerState = RenderStates.ScissorOnly;
            })) {
                PendingGroup = group;
                PendingRenderer = new ImperativeRenderer(group, Game.Materials, 0, autoIncrementSortKey: true, worldSpace: false, blendState: BlendState.AlphaBlend);
                // FIXME
                PendingRenderer.RasterUseUbershader = true;

                if (SceneBounds.HasValue) {
                    var sb = SceneBounds.Value;
                    Nuklear.nk_set_scene_bounds(
                        Context, sb.TopLeft.X, sb.TopLeft.Y,
                        sb.Size.X, sb.Size.Y
                    );
                }

                Scene();

                using (var e = Modals.GetEnumerator()) {
                    while (e.MoveNext()) {
                        if (!e.Current())
                            e.RemoveCurrent();
                    }
                }

                CurrentPaintIndex = 0;
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
            bool isActive, 
            MouseState previousMouseState, MouseState mouseState,
            KeyboardState previousKeyboardState, KeyboardState keyboardState,
            bool processMousewheel, IEnumerable<char> keystrokes = null
        ) {
            if (TextWidthCache.Count > 16 * 1024)
                TextWidthCache.Clear();

            foreach (var p in Pins)
                p.Free();

            Pins.Clear();
            PinnedObjects.Clear();

            NString.GC();

            var ctx = Context;
            Nuklear.nk_input_begin(ctx);

            if ((mouseState.X != previousMouseState.X) || (mouseState.Y != previousMouseState.Y))
                Nuklear.nk_input_motion(ctx, mouseState.X, mouseState.Y);

            var scrollDelta = (mouseState.ScrollWheelValue - previousMouseState.ScrollWheelValue) / 106f;
            if ((scrollDelta != 0) && processMousewheel)
                Nuklear.nk_input_scroll(ctx, new nk_vec2(0, scrollDelta));

            if ((mouseState.LeftButton != previousMouseState.LeftButton) && (isActive || mouseState.LeftButton == ButtonState.Released))
                Nuklear.nk_input_button(ctx, nk_buttons.NK_BUTTON_LEFT, mouseState.X, mouseState.Y, mouseState.LeftButton == ButtonState.Pressed ? 1 : 0);

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

            if (isActive) {
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

        public Tree CollapsingGroup (string caption, string name, bool defaultOpen = true, int hash = 0, string tooltip = null) {
            using (var tCaption = new NString(caption))
            using (var tName = new NString(name)) {
                var bounds = Nuklear.nk_widget_bounds(Context);
                var result = Nuklear.nk_tree_push_hashed(
                    Context, nk_tree_type.NK_TREE_TAB, tCaption.pText,
                    defaultOpen ? nk_collapse_states.NK_MAXIMIZED : nk_collapse_states.NK_MINIMIZED, tName.pText, tName.Length, hash
                );
                if (tooltip != null)
                    Tooltip(bounds, tooltip);
                return new Tree {
                    ctx = Context,
                    Visible = (result != 0)
                };
            }
        }

        public bool Property (string name, ref int value, int min, int max, int step, float inc_per_pixel, string tooltip = null) {
            using (var sName = new NString(name)) {
                var bounds = Nuklear.nk_widget_bounds(Context);
                var newValue = Nuklear.nk_propertyi(Context, sName.pText, min, value, max, step, inc_per_pixel);
                var result = newValue != value;
                value = newValue;
                Tooltip(bounds, tooltip);
                return result;
            }
        } 

        public bool Property (string name, ref float value, float min, float max, float step, float inc_per_pixel, string tooltip = null) {
            using (var sName = new NString(name)) {
                var bounds = Nuklear.nk_widget_bounds(Context);
                var newValue = Nuklear.nk_propertyf(Context, sName.pText, min, value, max, step, inc_per_pixel);
                var result = newValue != value;
                value = newValue;
                Tooltip(bounds, tooltip);
                return result;
            }
        } 

        public bool SelectableText (string name, bool state) {
            var flags = (uint)NkTextAlignment.NK_TEXT_LEFT;
            int selected = state ? 1 : 0;
            using (var s = new NString(name))
                Nuklear.nk_selectable_text(Context, s.pText, s.Length, flags, ref selected);
            return (selected != 0) && (state == false);
        }

        public unsafe bool Textbox (ref string text, string tooltip = null) {
            const int bufferSize = 4096;
            if ((text != null) && (text.Length >= bufferSize))
                throw new ArgumentOutOfRangeException("Text too long");

            var currentText = text ?? String.Empty;

            using (var buf1 = BufferPool<byte>.Allocate(bufferSize))
            using (var buf2 = BufferPool<byte>.Allocate(bufferSize)) {
                Array.Clear(buf1, 0, bufferSize);
                Array.Clear(buf2, 0, bufferSize);
                int byteLen = Encoding.UTF8.GetBytes(currentText, 0, Math.Min(currentText.Length, bufferSize - 1), buf1.Data, 0);
                Array.Copy(buf1.Data, buf2.Data, byteLen);
                int newByteLen = byteLen;

                fixed (byte* pBuf2 = buf2.Data) {
                    var flags = (uint)NkEditTypes.Field | (uint)NkEditFlags.AutoSelect;
                    var bounds = Nuklear.nk_widget_bounds(Context);
                    var res = Nuklear.nk_edit_string(Context, flags, pBuf2, &newByteLen, bufferSize - 1, null);
                    Tooltip(bounds, tooltip);
                    var changed = (newByteLen != byteLen + 1);
                    if (!changed) {
                        for (int i = 0; i < newByteLen; i++) {
                            if (buf1.Data[i] != buf2.Data[i]) {
                                changed = true;
                                break;
                            }
                        }
                    }
                    if (changed) {
                        var nullPos = Array.IndexOf(buf2.Data, (byte)0);
                        if (nullPos > 0)
                            newByteLen = Math.Min(newByteLen, nullPos);
                        // what the fuck
                        var bsPos = Array.IndexOf(buf2.Data, (byte)8);
                        if (bsPos > 0)
                            newByteLen = Math.Min(newByteLen, bsPos);
                        text = Encoding.UTF8.GetString(pBuf2, newByteLen);
                        return true;
                    }
                }
            }

            return false;
        }

        public GroupScrolled ScrollingGroup (float heightPx, string name, ref uint scrollX, ref uint scrollY) {
            using (var tName = new NString(name)) {
                uint flags = 0;
                NewRow(heightPx);
                var result = Nuklear.nk_group_scrolled_offset_begin(Context, ref scrollX, ref scrollY, tName.pText, flags);

                return new GroupScrolled {
                    ctx = Context,
                    Visible = result != 0
                };
            }
        }

        public void Label (string text, bool centered = false) {
            NkTextAlign flags = NkTextAlign.NK_TEXT_ALIGN_MIDDLE;
            flags |= centered ? NkTextAlign.NK_TEXT_ALIGN_CENTERED : NkTextAlign.NK_TEXT_ALIGN_LEFT;
            using (var s = new NString(text))
                Nuklear.nk_label(Context, s.pText, (uint)flags);
        }

        public void Tooltip (NkRect bounds, string text) {
            if (text == null)
                return;

            if (Nuklear.nk_input_is_mouse_hovering_rect(&Context->input, bounds) != 0) {
                using (var utf8 = new NString(text))
                    Nuklear.nk_tooltip(Context, utf8.pText);
            }
        }

        public bool Button (string text, bool enabled = true, string tooltip = null) {
            var bounds = Nuklear.nk_widget_bounds(Context);
            var result = false;
            if (enabled)
                using (var s = new NString(text))
                    result = Nuklear.nk_button_label(Context, s.pText) != 0;
            else
                Label(text, true);
            Tooltip(bounds, tooltip);
            return result;
        }

        // Returns true if value changed
        public unsafe bool Checkbox (string text, ref bool value, string tooltip = null) {
            var bounds = Nuklear.nk_widget_bounds(Context);
            bool newValue;
            using (var temp = new NString(text))
                newValue = Nuklear.nk_check_text(Context, temp.pText, temp.Length, value ? 0 : 1) == 0;

            var result = newValue != value;
            value = newValue;
            Tooltip(bounds, tooltip);
            return result;
        }

        public bool ComboBox (ref int selectedIndex, Func<int, string> getter, int count, string tooltip = null) {
            var rect = Nuklear.nk_layout_space_bounds(Context);
            var strings = new List<NString>();
            nk_item_getter_fun wrappedGetter = (user, i, idk) => {
                var text = getter(i);
                var str = new NString(text);
                *idk = str.pText;
                strings.Add(str);
            };
            var bounds = Nuklear.nk_widget_bounds(Context);
            var oldIndex = selectedIndex;
            Nuklear.nk_combobox_callback(
                Context, wrappedGetter, IntPtr.Zero, 
                ref selectedIndex, count, (int)_Font.LineSpacing + 1, new NuklearDotNet.nk_vec2(rect.W - 32, 512)
            );
            foreach (var s in strings)
                s.Dispose();
            Tooltip(bounds, tooltip);
            return (oldIndex != selectedIndex);
        }

        public bool EnumCombo (ref object value, Type type = null, string[] names = null, string tooltip = null) {
            if (type == null)
                type = value.GetType();
            if (names == null)
                names = Enum.GetNames(type);
            var name = Enum.GetName(type, value);
            var selectedIndex = Array.IndexOf(names, name);
            if (ComboBox(
                ref selectedIndex, 
                (i) =>
                    (i >= 0) && (i < names.Length)
                        ? names[i]
                        : "",
                names.Length, tooltip: tooltip
            )) {
                var newName = names[selectedIndex];
                value = Enum.Parse(type, newName, true);
                return true;
            }
            return false;
        }

        public void NewRow (float lineHeight, int columnCount = 1) {
            Nuklear.nk_layout_row_dynamic(Context, lineHeight, columnCount);
        }

        private readonly List<GCHandle> Pins = new List<GCHandle>();
        private readonly List<object> PinnedObjects = new List<object>();

        public unsafe void* PinForOneFrame (object obj) {
            var handle = GCHandle.Alloc(obj, GCHandleType.Pinned);
            Pins.Add(handle);
            PinnedObjects.Add(obj);
            return handle.AddrOfPinnedObject().ToPointer();
        }

        public void Dispose () {
            // FIXME
        }

        public bool CustomPanel (float requestedHeight, out Bounds bounds) {
            NewRow(requestedHeight);
            var rect = new NkRect { W = 9999, H = requestedHeight };
            var state = Nuklear.nk_widget(&rect, Context);
            bounds = Bounds.FromPositionAndSize(rect.X, rect.Y, rect.W, rect.H);
            return state != NuklearDotNet.nk_widget_layout_states.NK_WIDGET_INVALID;
        }
    }

    public unsafe struct NString : IDisposable {
        private static ThreadLocal<Encoder> Encoder = new ThreadLocal<Encoder>(
            () => Encoding.UTF8.GetEncoder()
        );

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
            var encoder = Encoder.Value;
            fixed (char* pChars = text) {
                encoder.Reset();
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