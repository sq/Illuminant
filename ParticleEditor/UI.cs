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
        private int NextMatrixIndex;

        public int SidePanelWidth {
            get {
                var w = Graphics.PreferredBackBufferWidth;
                if (w >= 2000)
                    return 675;
                else
                    return 500;
            }
        }

        protected unsafe void UIScene () {
            var ctx = Nuklear.Context;
            NextMatrixIndex = 0;
            
            using (var wnd = Nuklear.Window(
                "SidePanel",
                Bounds.FromPositionAndSize(
                    Graphics.PreferredBackBufferWidth - SidePanelWidth, 0, 
                    SidePanelWidth, Graphics.PreferredBackBufferHeight
                ),
                NuklearDotNet.NkPanelFlags.Border
            )) {
                if (wnd.Visible)
                    RenderSidePanels();
            }

            IsMouseOverUI = Nuke.nk_item_is_any_active(ctx) != 0;
            if (IsMouseOverUI)
                LastTimeOverUI = Squared.Util.Time.Ticks;
        }

        private void RenderSidePanels () {
            RenderFilePanel();
            RenderSystemList();
            RenderTransformList();
            RenderTransformProperties();
            RenderGlobalSettings();
        }

        private unsafe void RenderFilePanel () {
            var ctx = Nuklear.Context;

            using (var group = Nuklear.CollapsingGroup("File", "File", true))
            if (group.Visible) {
                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, Model.Filename == null ? 2 : 3);
                if (Model.Filename != null) {
                    if (Nuklear.Button("Save"))
                        RunWorkItem(() => Model.Save(Model.Filename));
                    if (Nuklear.Button("Save As"))
                        Controller.ShowSaveDialog();
                } else {
                    if (Nuklear.Button("Save"))
                        Controller.ShowSaveDialog();
                }
                if (Nuklear.Button("Load"))
                    Controller.ShowLoadDialog();
            }
        }

        private unsafe void RenderSystemList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Systems", "Systems"))
            if (group.Visible) {
                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, Model.Systems.Count > 0 ? 3 : 2);
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
            RenderPropertyGrid(Controller.SelectedSystem.Model.Configuration, ref SystemProperties, null);
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
                int typeIndex = TransformTypes.IndexOf(xform.Model.Type);

                if (Nuklear.ComboBox(ref typeIndex, (i) => TransformTypes[i].Name, TransformTypes.Count)) {
                    Controller.ChangeTransformType(xform, TransformTypes[typeIndex]);
                } else {
                    if (TransformProperties.Prepare(xform.Model.Type)) {
                        foreach (var m in TransformProperties.Members) {
                            var name = m.Name;
                            var setter = m.Setter;
                            m.Setter = (i, v) => {
                                Controller.SelectedTransform.Model.Properties[name] = 
                                    ModelProperty.New(v);
                                setter(i, v);
                            };
                        }
                    }
                    RenderPropertyGrid(xform.Instance, ref TransformProperties, null);
                }
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

                Checkbox("Statistics", ref ShowPerformanceStats);

                Nuke.nk_label(ctx, "Zoom", (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                Zoom = Nuke.nk_slide_float(ctx, 0.5f, Zoom, 2.0f, 0.1f);

                Nuke.nk_tree_pop(ctx);
            }
        }
    }
}
