using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Squared.Render;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        protected unsafe void UIScene () {
            var ctx = Nuklear.Context;

            var panelWidth = 400;
            
            var isWindowOpen = Nuke.nk_begin(
                ctx, "SidePanel", new NuklearDotNet.NkRect(Graphics.PreferredBackBufferWidth - panelWidth, 0, panelWidth, Graphics.PreferredBackBufferHeight),
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
            RenderGlobalSettings();
        }

        UTF8String sGlobalSettings = new UTF8String("Global Settings");

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
            }

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

            using (var group = Nuklear.CollapsingGroup("Transform Properties", "Transform Properties", 4))
            if (group.Visible && (Controller.SelectedTransform != null)) {
            }
        }

        private unsafe void RenderGlobalSettings () {
            var ctx = Nuklear.Context;

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
            using (var temp = new UTF8String(text)) {
                var newValue = Nuke.nk_check_text(Nuklear.Context, temp.pText, temp.Length, value ? 0 : 1) == 0;
                var result = newValue != value;
                value = newValue;
                return result;
            }
        }
    }
}
