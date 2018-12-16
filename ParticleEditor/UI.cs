using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Render;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        private class CachedPropertyInfo {
            public string Name;
            public FieldInfo Field;
            public PropertyInfo Property;
            public Type Type;
            public Func<object, object> Getter;
            public Action<object, object> Setter;
        }

        private struct PropertyGridCache {
            public Type CachedType;
            public List<CachedPropertyInfo> Members;

            public uint ScrollX, ScrollY;
            public string SelectedPropertyName;

            internal bool Prepare (Type type) {
                if (type == CachedType)
                    return false;

                CachedType = type;
                Members = CachePropertyInfo(type).ToList();
                SelectedPropertyName = null;
                return true;
            }
        }

        private List<CachedPropertyInfo> FormulaMembers = CachePropertyInfo(typeof(Formula)).ToList();
        private PropertyGridCache SystemProperties, TransformProperties;

        protected unsafe void UIScene () {
            var ctx = Nuklear.Context;

            var panelWidth = 550;
            
            using (var wnd = Nuklear.Window(
                "SidePanel",
                Bounds.FromPositionAndSize(
                    Graphics.PreferredBackBufferWidth - panelWidth, 0, 
                    panelWidth, Graphics.PreferredBackBufferHeight
                ),
                NuklearDotNet.NkPanelFlags.Border
            )) {
                if (wnd.Visible)
                    RenderSidePanels();
            }

            if (false)
            using (var wnd = Nuklear.Window(
                "Select Texture",
                Bounds.FromPositionAndSize(100, 100, 640, 480),
                NuklearDotNet.NkPanelFlags.BorderTitle |
                NuklearDotNet.NkPanelFlags.Movable
            )) {
                if (wnd.Visible)
                    RenderTexturePicker();
            }

            IsMouseOverUI = Nuke.nk_item_is_any_active(ctx) != 0;
            if (IsMouseOverUI)
                LastTimeOverUI = Squared.Util.Time.Ticks;
        }

        private void RenderTexturePicker () {
        }

        private void RenderSidePanels () {
            RenderSystemList();
            RenderTransformList();
            RenderTransformProperties();
            RenderGlobalSettings();
        }

        private unsafe void RenderSystemList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Systems", "Systems"))
            if (group.Visible) {
                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                if (Nuklear.Button("Add"))
                    Controller.AddSystem();
                if (Nuklear.Button("Remove"))
                    Controller.RemoveSystem(state.Systems.SelectedIndex);

                using (var list = Nuklear.ScrollingGroup(80, "System List", ref state.Systems.ScrollX, ref state.Systems.ScrollY))
                if (list.Visible) {
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing, 1);
                    for (int i = 0; i < Model.Systems.Count; i++) {
                        var system = Model.Systems[i];
                        if (Nuklear.SelectableText(system.Name ?? string.Format("{0} Unnamed", i), state.Systems.SelectedIndex == i))
                            state.Systems.SelectedIndex = i;
                    }
                }
            }

            using (var group = Nuklear.CollapsingGroup("System Properties", "System Properties", false))
            if (group.Visible && (Controller.SelectedSystem != null))
                RenderSystemProperties();
        }

        private unsafe void RenderSystemProperties () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;
            var s = Controller.SelectedSystem;
            var i = s.Instance;

            Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
            var time = TimeSpan.FromTicks(s.Time.Ticks);
            using (var tCount = new NString(time.ToString()))
                Nuke.nk_text(ctx, tCount.pText, tCount.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
            if (Nuklear.Button("Reset"))
                Controller.QueueReset(s);

            Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 1);
            using (var tCount = new NString(string.Format("{0}/{1}", i.LiveCount, i.Capacity)))
                Nuke.nk_text(ctx, tCount.pText, tCount.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);

            SystemProperties.Prepare(typeof (ParticleSystemConfiguration));
            RenderPropertyGrid(Controller.SelectedSystem.Model.Configuration, ref SystemProperties, 500);
        }

        private unsafe void RenderTransformList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Transforms", "Transforms"))
            if (group.Visible && (Controller.SelectedSystem != null)) {
                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                if (Nuklear.Button("Add"))
                    Controller.AddTransform();
                if (Nuklear.Button("Remove"))
                    Controller.RemoveTransform(state.Transforms.SelectedIndex);

                var view = Controller.View.Systems[state.Systems.SelectedIndex];
                var model = view.Model;

                using (var list = Nuklear.ScrollingGroup(140, "Transform List", ref state.Transforms.ScrollX, ref state.Transforms.ScrollY))
                if (list.Visible) {
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing, 1);
                    for (int i = 0; i < model.Transforms.Count; i++) {
                        var xform = model.Transforms[i];
                        if (Nuklear.SelectableText(xform.Name ?? string.Format("{0} {1}", i, xform.Type.Name), state.Transforms.SelectedIndex == i))
                            state.Transforms.SelectedIndex = i;
                    }
                }
            }
        }

        private unsafe void RenderTransformProperties () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;
            var xform = Controller.SelectedTransform;

            using (var group = Nuklear.CollapsingGroup("Transform Properties", "Transform Properties"))
            if (group.Visible && (xform != null)) {
                if (TransformProperties.Prepare(xform.Model.Type)) {
                    foreach (var m in TransformProperties.Members) {
                        var name = m.Name;
                        var setter = m.Setter;
                        m.Setter = (i, v) => {
                            Controller.SelectedTransform.Model.Properties[name] = v;
                            setter(i, v);
                        };
                    }
                }
                RenderPropertyGrid(xform.Instance, ref TransformProperties, 500);
            }
        }

        private static IEnumerable<CachedPropertyInfo> CachePropertyInfo (Type type) {
            return from m in type.GetMembers(BindingFlags.Instance | BindingFlags.Public)
                   where (m.MemberType == MemberTypes.Field) || (m.MemberType == MemberTypes.Property)
                   let f = m as FieldInfo
                   let p = m as PropertyInfo
                   where (f == null) || !f.IsInitOnly
                   where (p == null) || p.CanWrite
                   where !m.GetCustomAttributes<NonSerializedAttribute>().Any()
                   orderby m.Name
                   select new CachedPropertyInfo {
                       Name = m.Name,
                       Field = f,
                       Property = p,
                       Type = (f != null) ? f.FieldType : p.PropertyType,
                       Getter = (f != null) ? (Func<object, object>)f.GetValue : p.GetValue,
                       Setter = (f != null) ? (Action<object, object>)f.SetValue : p.SetValue
                   };
        }

        private unsafe void RenderPropertyGrid (object instance, ref PropertyGridCache cache, float heightPx) {
            using (var g = Nuklear.ScrollingGroup(heightPx, "Properties", ref cache.ScrollX, ref cache.ScrollY))
            if (g.Visible) {
                foreach (var cpi in cache.Members)
                    RenderProperty(ref cache, cpi, instance);
            }
        }

        private unsafe bool RenderProperty (
            ref PropertyGridCache cache,
            CachedPropertyInfo cpi,
            object instance,
            string prefix = null
        ) {
            bool a, b, c, d;
            var ctx = Nuklear.Context;
            var actualName = cpi.Name;
            if (!string.IsNullOrEmpty(prefix))
                actualName = prefix + actualName;

            var isActive = cache.SelectedPropertyName == actualName;
            var value = cpi.Getter(instance);

            switch (cpi.Type.Name) {
                case "Formula":
                    using (var pGroup = Nuklear.CollapsingGroup(cpi.Name, actualName, false)) {
                        var changed = false;
                        if (pGroup.Visible) {
                            foreach (var m in FormulaMembers)
                                changed = RenderProperty(ref cache, m, value, cpi.Name) || changed;

                            if (changed)
                                cpi.Setter(instance, value);
                        }
                        return changed;
                    }

                case "Int32":
                case "Single":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 1);
                    if (cpi.Type == typeof(float)) {
                        var v = (float)value;
                        if (Nuklear.Property(cpi.Name, ref v, 0, 4096, 8, 0.5f)) {
                            cpi.Setter(instance, v);
                            return true;
                        }
                    } else {
                        var v = (int)value;
                        if (Nuklear.Property(cpi.Name, ref v, 0, 4096, 8, 1)) {
                            cpi.Setter(instance, v);
                            return true;
                        }
                    }

                    return false;
            }

            Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
            Nuklear.SelectableText(cpi.Name, isActive);

            if (value == null) {
                Nuklear.SelectableText("null", isActive);
                return false;
            }

            switch (cpi.Type.Name) {
                case "String":
                    if (Nuklear.SelectableText(value.ToString(), isActive))
                        cache.SelectedPropertyName = actualName;
                    return false;

                case "Boolean":
                    b = (bool)value;
                    if (Checkbox(null, ref b)) {
                        cpi.Setter(instance, b);
                        return true;
                    }
                    return false;

                case "Vector2":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                    var v2 = (Vector2)value;
                    a = Nuklear.Property("#x", ref v2.X, -1, 1, 0.1f, 0.01f);
                    b = Nuklear.Property("#y", ref v2.Y, -1, 1, 0.1f, 0.01f);
                    if (a || b) {
                        cpi.Setter(instance, v2);
                        return true;
                    }
                    return false;

                case "Vector3":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 3);
                    var v3 = (Vector3)value;
                    a = Nuklear.Property("#x", ref v3.X, -1, 1, 0.1f, 0.01f);
                    b = Nuklear.Property("#y", ref v3.Y, -1, 1, 0.1f, 0.01f);
                    c = Nuklear.Property("#z", ref v3.Z, -1, 1, 0.1f, 0.01f);
                    if (a || b || c) {
                        cpi.Setter(instance, v3);
                        return true;
                    }
                    return false;

                case "Vector4":
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 4);
                    var v4 = (Vector4)value;
                    a = Nuklear.Property("#x", ref v4.X, -1, 1, 0.1f, 0.01f);
                    b = Nuklear.Property("#y", ref v4.Y, -1, 1, 0.1f, 0.01f);
                    c = Nuklear.Property("#z", ref v4.Z, -1, 1, 0.1f, 0.01f);
                    d = Nuklear.Property("#w", ref v4.W, -1, 1, 0.1f, 0.01f);
                    if (a || b || c || d) {
                        cpi.Setter(instance, v4);
                        return true;
                    }
                    return false;

                default:
                    if (Nuklear.SelectableText(value.GetType().Name, isActive))
                        cache.SelectedPropertyName = actualName;
                    return false;
            }
        }

        private unsafe void RenderGlobalSettings () {
            var ctx = Nuklear.Context;

            using (var sGlobalSettings = new NString("Global Settings"))
            if (Nuke.nk_tree_push_hashed(ctx, NuklearDotNet.nk_tree_type.NK_TREE_TAB, sGlobalSettings.pText, NuklearDotNet.nk_collapse_states.NK_MINIMIZED, sGlobalSettings.pText, sGlobalSettings.Length, 256) != 0) {
                var vsync = Graphics.SynchronizeWithVerticalRetrace;
                if (Checkbox("VSync", ref vsync)) {
                    Graphics.SynchronizeWithVerticalRetrace = vsync;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                }

                var fullscreen = Graphics.IsFullScreen;
                if (Checkbox("Fullscreen", ref fullscreen))
                    SetFullScreen(fullscreen);

                Checkbox("Show Statistics", ref ShowPerformanceStats);

                Nuke.nk_tree_pop(ctx);
            }
        }


        // Returns true if value changed
        private unsafe bool Checkbox (string text, ref bool value) {
            bool newValue;
            using (var temp = new NString(text))
                newValue = Nuke.nk_check_text(Nuklear.Context, temp.pText, temp.Length, value ? 0 : 1) == 0;

            var result = newValue != value;
            value = newValue;
            return result;
        }
    }
}
