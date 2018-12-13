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

        UTF8String sSystems = new UTF8String("Systems");
        UTF8String sGlobalSettings = new UTF8String("Global Settings");
        UTF8String sSystemList = new UTF8String("System List");

        private unsafe void RenderSystemList () {
            var ctx = Nuklear.Context;

            if (
                Nuke.nk_tree_push_hashed(
                    ctx, NuklearDotNet.nk_tree_type.NK_TREE_TAB, sSystems.pText, 
                    NuklearDotNet.nk_collapse_states.NK_MAXIMIZED, sSystems.pText, sSystems.Length, 1
                ) != 0
            ) {
                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing + 2, 2);
                Nuke.nk_button_label(ctx, "Add");
                Nuke.nk_button_label(ctx, "Remove");

                Nuke.nk_layout_row(ctx, NuklearDotNet.nk_layout_format.NK_DYNAMIC, 150, 1, new[] { 1.0f });
                Nuke.nk_group_scrolled_offset_begin(ctx, ref Controller.CurrentState.SystemListX, ref Controller.CurrentState.SystemListY, sSystemList.pText, 0);

                Nuke.nk_layout_row_dynamic(ctx, Font.LineSpacing, 1);
                var flags = (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT;
                var items = new[] { "item 1", "item 2", "item 3", "item 4", "item 5" };
                for (int i = 0; i < items.Length; i++) {
                    var item = items[i];
                    int selected = (Controller.CurrentState.SelectedSystemIndex == i) ? 1 : 0;
                    using (var s = new UTF8String(item))
                        Nuke.nk_selectable_text(ctx, s.pText, s.Length, flags, ref selected);
                    if (selected != 0)
                        Controller.CurrentState.SelectedSystemIndex = i;
                }

                Nuke.nk_group_scrolled_end(ctx);

                Nuke.nk_tree_pop(ctx);
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
