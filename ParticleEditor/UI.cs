using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Squared.Render;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        private struct PropertyGridCache {
            public object Instance;
            public SortedDictionary<string, MemberInfo> Members;
        }

        private PropertyGridCache SystemProperties, TransformProperties;

        protected unsafe void UIScene () {
            var ctx = Nuklear.Context;

            var panelWidth = 400;
            
            var isWindowOpen = Nuke.nk_begin(
                ctx, "SidePanel", new NuklearDotNet.NkRect(
                    Graphics.PreferredBackBufferWidth - panelWidth, 0, 
                    panelWidth, Graphics.PreferredBackBufferHeight
                ),
                (uint)(NuklearDotNet.NkPanelFlags.Border)
            ) != 0;

            if (isWindowOpen)
                RenderSidePanels();

            IsMouseOverUI = Nuke.nk_item_is_any_active(ctx) != 0;
            if (IsMouseOverUI)
                LastTimeOverUI = Time.Ticks;

            Nuke.nk_end(ctx);
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

            using (var group = Nuklear.CollapsingGroup("Systems", "Systems", 1))
            if (group.Visible) {
                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                if (Nuklear.Button("Add"))
                    Controller.AddSystem();
                if (Nuklear.Button("Remove"))
                    Controller.RemoveSystem(state.Systems.SelectedIndex);

                using (var list = Nuklear.ScrollingGroup(150, "System List", ref state.Systems.ScrollX, ref state.Systems.ScrollY)) {
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing, 1);
                    for (int i = 0; i < Model.Systems.Count; i++) {
                        var system = Model.Systems[i];
                        if (Nuklear.SelectableText(system.Name ?? string.Format("{0} Unnamed", i), state.Systems.SelectedIndex == i))
                            state.Systems.SelectedIndex = i;
                    }
                }
            }

            using (var group = Nuklear.CollapsingGroup("System Properties", "System Properties", 2))
            if (group.Visible && (Controller.SelectedSystem != null)) {
                var s = Controller.SelectedSystem.Instance;
                using (var tCount = new UTF8String(string.Format("{0}/{1}", s.LiveCount, s.Capacity)))
                    Nuke.nk_text(ctx, tCount.pText, tCount.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                RenderPropertyGrid(Controller.SelectedSystem.Model.Configuration, ref SystemProperties);
            }
        }

        private unsafe void RenderTransformList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Transforms", "Transforms", 3))
            if (group.Visible && (Controller.SelectedSystem != null)) {
                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                if (Nuklear.Button("Add"))
                    Controller.AddTransform();
                if (Nuklear.Button("Remove"))
                    Controller.RemoveTransform(state.Transforms.SelectedIndex);

                var view = Controller.View.Systems[state.Systems.SelectedIndex];
                var model = view.Model;

                using (var list = Nuklear.ScrollingGroup(150, "Transform List", ref state.Transforms.ScrollX, ref state.Transforms.ScrollY)) {
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

            using (var group = Nuklear.CollapsingGroup("Transform Properties", "Transform Properties", 4))
            if (group.Visible && (xform != null))
                RenderPropertyGrid(xform.Instance, ref TransformProperties);
        }

        private unsafe void RenderPropertyGrid (object instance, ref PropertyGridCache cache) {
            if (cache.Instance != instance) {
                cache.Instance = instance;
                cache.Members = new SortedDictionary<string, MemberInfo>();
                var seq = from m in instance.GetType().GetMembers(BindingFlags.Instance | BindingFlags.Public)
                            where (m.MemberType == MemberTypes.Field) || (m.MemberType == MemberTypes.Property)
                            let f = m as FieldInfo
                            let p = m as PropertyInfo
                            where (f == null) || !f.IsInitOnly
                            where (p == null) || p.CanWrite
                            where !m.GetCustomAttributes<NonSerializedAttribute>().Any()
                            select new KeyValuePair<string, MemberInfo>(m.Name, m);
                foreach (var kvp in seq)
                    cache.Members.Add(kvp.Key, kvp.Value);
            }

            foreach (var kvp in cache.Members) {
                Type type;
                object value = null;
                var prop = kvp.Value as PropertyInfo;
                var field = kvp.Value as FieldInfo;
                Action<object, object> setter;
                if (prop != null) {
                    type = prop.PropertyType;
                    value = prop.GetValue(instance);
                    setter = prop.SetValue;
                } else if (field != null) {
                    type = field.FieldType;
                    value = field.GetValue(instance);
                    setter = field.SetValue;
                } else {
                    continue;
                }

                RenderProperty(instance, kvp.Key, type, value, setter);
            }
        }

        private unsafe void RenderProperty (
            object instance, 
            string name, Type type, 
            object value, Action<object, object> setter
        ) {
            var ctx = Nuklear.Context;
            var isActive = false;

            Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
            Nuklear.SelectableText(name, isActive);

            if (value == null) {
                Nuklear.SelectableText("null", isActive);
                return;
            }

            switch (type.Name) {
                case "String":
                    Nuklear.SelectableText(value.ToString(), isActive);
                    return;
                case "Int32":
                case "Single":
                    Nuklear.SelectableText(value.ToString(), isActive);
                    Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 1);
                    if (type == typeof(float)) {
                        var v = Convert.ToSingle(value);
                        if (v > 4096)
                            v = 4096;
                        var newValue = Nuke.nk_slide_float(ctx, 0, v, 4096, 8);
                        if (newValue != v)
                            setter(instance, newValue);
                    } else {
                        var v = Convert.ToInt32(value);
                        if (v > 4096)
                            v = 4096;
                        var newValue = Nuke.nk_slide_int(ctx, 0, v, 4096, 8);
                        if (newValue != v)
                            setter(instance, newValue);
                    }
                    return;
                case "Boolean":
                    var b = (bool)value;
                    if (Checkbox(null, ref b))
                        setter(instance, b);
                    return;
                default:
                    Nuklear.SelectableText("", isActive);
                    return;
            }
        }

        private unsafe void RenderGlobalSettings () {
            var ctx = Nuklear.Context;

            using (var sGlobalSettings = new UTF8String("Global Settings"))
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
            using (var temp = new UTF8String(text))
                newValue = Nuke.nk_check_text(Nuklear.Context, temp.pText, temp.Length, value ? 0 : 1) == 0;

            var result = newValue != value;
            value = newValue;
            return result;
        }
    }
}
