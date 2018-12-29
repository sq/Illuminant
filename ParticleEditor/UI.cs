﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Framework;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Render;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class PropertyEditor {
        private int NextMatrixIndex;

        public int SidePanelWidth {
            get {
                var w = FrameBufferWidth;
                if (w >= 2000)
                    return 675;
                else
                    return 525;
            }
        }

        internal unsafe void UIScene () {
            var ctx = Nuklear.Context;
            NextMatrixIndex = 0;
            
            using (var wnd = Nuklear.Window(
                "SidePanel",
                Bounds.FromPositionAndSize(
                    FrameBufferWidth - SidePanelWidth, 0, 
                    SidePanelWidth, FrameBufferHeight
                ),
                NuklearDotNet.NkPanelFlags.Border
            )) {
                if (wnd.Visible)
                    RenderSidePanels();
            }

            Game.IsMouseOverUI = Nuke.nk_item_is_any_active(ctx) != 0;
            if (Game.IsMouseOverUI)
                Game.LastTimeOverUI = Squared.Util.Time.Ticks;
        }

        private void RenderSidePanels () {
            RenderFilePanel();
            if (Game.View != null) {
                RenderSystemList();
                RenderTransformList();
                RenderTransformProperties();
            }
            RenderGlobalSettings();
        }

        public float LineHeight {
            get {
                return (float)Math.Ceiling(Game.Font.LineSpacing) + 3;
            }
        }

        private unsafe void RenderFilePanel () {
            var ctx = Nuklear.Context;

            /*
            using (var group = Nuklear.CollapsingGroup("File", "File", true))
            if (group.Visible) {
            */
            Nuke.nk_layout_row_dynamic(ctx, LineHeight, Model.Filename == null ? 2 : 3);
            if (Model.Filename != null) {
                if (Nuklear.Button("Save")) {
                    RunWorkItem(() => Model.Save(Model.Filename));
                }
                if (Nuklear.Button("Save As"))
                    Controller.ShowSaveDialog();
            } else {
                if (Nuklear.Button("Save"))
                    Controller.ShowSaveDialog();
            }
            if (Nuklear.Button("Load"))
                Controller.ShowLoadDialog();

            if (Game.View != null) {
                Nuke.nk_layout_row_dynamic(ctx, LineHeight, 3);
                var time = TimeSpan.FromTicks(Game.View.Time.Ticks);
                using (var tCount = new NString(time.ToString("hh\\:mm\\:ss\\.ff")))
                    Nuke.nk_text(ctx, tCount.pText, tCount.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                if (Nuklear.Button("Restart"))
                    Controller.QueueReset();
                if (Nuklear.Button(Controller.Paused ? "Unpause" : "Pause"))
                    Controller.Paused = !Controller.Paused;
                Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
                var liveCount = Game.View.Systems.Sum(s => s.Instance.LiveCount);
                var capacity = Game.View.Systems.Sum(s => s.Instance.Capacity);
                var memory = Game.View.Engine.EstimateMemoryUsage();
                using (var tCount = new NString(string.Format("{0}/{1}", liveCount, capacity)))
                    Nuke.nk_text(ctx, tCount.pText, tCount.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                using (var tMemory = new NString(string.Format("{0:0000.00}MB", memory / (1024 * 1024.0))))
                    Nuke.nk_text(ctx, tMemory.pText, tMemory.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_RIGHT);
            }

            // }
        }

        private unsafe void RenderSystemList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Systems", "Systems"))
            if (group.Visible) {
                Nuke.nk_layout_row_dynamic(ctx, LineHeight, 2);
                if (Nuklear.Button("Add"))
                    Controller.AddSystem();
                if (Nuklear.Button("Remove", Model.Systems.Count > 0))
                    Controller.RemoveSystem(state.Systems.SelectedIndex);

                using (var list = Nuklear.ScrollingGroup(80, "System List", ref state.Systems.ScrollX, ref state.Systems.ScrollY))
                if (list.Visible) {
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 1);
                    for (int i = 0; i < Model.Systems.Count; i++) {
                        var system = Model.Systems[i];
                        if (Nuklear.SelectableText(
                            string.IsNullOrWhiteSpace(system.Name) ? string.Format("{0} Unnamed", i) : system.Name, 
                            state.Systems.SelectedIndex == i
                        ))
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

            Nuke.nk_layout_row_dynamic(ctx, LineHeight + 3, 1);

            Nuklear.Textbox(ref s.Model.Name);

            var config = Controller.SelectedSystem.Model.Configuration;
            SystemProperties.Prepare(config);
            RenderPropertyGrid(config, SystemProperties, null);
        }

        private unsafe void RenderTransformList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Transforms", "Transforms"))
            if (group.Visible && (Controller.SelectedSystem != null)) {
                Nuke.nk_layout_row_dynamic(ctx, LineHeight, 3);
                if (Nuklear.Button("Add"))
                    Controller.AddTransform();
                if (Nuklear.Button("Remove", Controller.SelectedSystem.Transforms.Count > 0)) {
                    Controller.RemoveTransform(state.Transforms.SelectedIndex);
                    if (state.Transforms.SelectedIndex > 0)
                        state.Transforms.SelectedIndex--;
                }
                if (Nuklear.Button("Duplicate", Controller.SelectedSystem.Transforms.Count > 0))
                    Controller.DuplicateTransform(state.Transforms.SelectedIndex);

                var view = Controller.View.Systems[state.Systems.SelectedIndex];
                var model = view.Model;

                using (var list = Nuklear.ScrollingGroup(140, "Transform List", ref state.Transforms.ScrollX, ref state.Transforms.ScrollY))
                if (list.Visible) {
                    Nuke.nk_layout_row_dynamic(ctx, LineHeight, 1);
                    for (int i = 0; i < model.Transforms.Count; i++) {
                        var xform = model.Transforms[i];
                        string displayName = !string.IsNullOrWhiteSpace(xform.Name)
                            ? string.Format("{1}: {0}", xform.Type.Name, xform.Name)
                            : string.Format("#{1}: {0}", xform.Type.Name, i);
                        if (Nuklear.SelectableText(displayName, state.Transforms.SelectedIndex == i))
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
                Nuke.nk_layout_row_dynamic(ctx, LineHeight + 3, 1);

                Nuklear.Textbox(ref xform.Model.Name);

                int typeIndex = TransformTypes.IndexOf(xform.Model.Type);

                if (Nuklear.ComboBox(ref typeIndex, (i) => TransformTypes[i].Name, TransformTypes.Count)) {
                    Controller.ChangeTransformType(xform, TransformTypes[typeIndex]);
                } else {
                    if (TransformProperties.Prepare(xform.Model, xform.Model.Type)) {
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
                    RenderPropertyGrid(xform.Instance, TransformProperties, null);
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
                    Graphics.ApplyChangesAfterPresent(Game.RenderCoordinator);
                }

                var fullscreen = Graphics.IsFullScreen;
                if (Checkbox("Fullscreen", ref fullscreen))
                    Game.SetFullScreen(fullscreen);

                Checkbox("Statistics", ref Game.ShowPerformanceStats);

                Nuke.nk_label(ctx, "Zoom", (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                Game.Zoom = Nuke.nk_slide_float(ctx, ParticleEditor.MinZoom, Game.Zoom, ParticleEditor.MaxZoom, 0.025f);

                Nuke.nk_label(ctx, "Background Brightness", (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                Game.Brightness = Nuke.nk_slide_float(ctx, 0, Game.Brightness, 1, 0.01f);

                Nuke.nk_tree_pop(ctx);
            }
        }
    }
}
