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
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Render;
using Nuke = NuklearDotNet.Nuklear;

namespace Lumined {
    public partial class PropertyEditor {
        private int NextMatrixIndex;

        private static Type[] ValidVariableTypes = new[] {
            typeof(float),
            typeof(Vector2),
            typeof(Vector3),
            typeof(Vector4),
            typeof(Squared.Illuminant.Configuration.DynamicMatrix)
        };
        private string[] ValidVariableTypeNames = (from ct in ValidVariableTypes select ct.Name).ToArray();

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
            NameStack.Clear();
            
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
                Controller.SelectedProperty = Controller.NewSelectedProperty;
                Controller.NewSelectedProperty = null;
            }

            Game.IsMouseOverUI = Nuke.nk_item_is_any_active(ctx) != 0;
            if (Game.IsMouseOverUI)
                Game.LastTimeOverUI = Squared.Util.Time.Ticks;
        }

        private void RenderSidePanels () {
            RenderFilePanel();
            RenderDocumentProperties();
            RenderVariableList();
            if (Game.View != null)
                RenderSystemList();
            if (Game.View != null) {
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
            var buttonCount = Model.Filename == null ? 3 : 4;
            var enableNew = (Model.Systems.Count > 0) || (Model.NamedVariables.Count > 0);
            Nuklear.NewRow(LineHeight, buttonCount);
            if (Nuklear.Button("New", enableNew)) {
                Controller.SetModel(Game.CreateNewModel());
                Controller.AddSystem();
            }
            if (Model.Filename != null) {
                if (Nuklear.Button("Save")) {
                    RunWorkItem(() => {
                        Model.UserData["ControllerState"] = Controller.CurrentState.Clone();
                        Model.Save(Model.Filename);
                    });
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
                Nuklear.NewRow(LineHeight, 4);
                var time = TimeSpan.FromTicks(Game.View.Time.Ticks);
                using (var tCount = new NString(time.ToString("mm\\:ss\\.ff")))
                    Nuke.nk_text(ctx, tCount.pText, tCount.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                if (Nuklear.Button("Restart"))
                    Controller.QueueReset();
                if (Nuklear.Button(Controller.Paused ? "Unpause" : "Pause"))
                    Controller.Paused = !Controller.Paused;
                if (Nuklear.Button("Step", Controller.Paused))
                    Controller.Step();
                Nuklear.NewRow(LineHeight, 2);
                var liveCount = Game.View.Systems.Sum(s => s.Instance.LiveCount);
                var capacity = Game.View.Systems.Sum(s => s.Instance.Capacity);
                if (Game.View.Engine != null) {
                    var memory = Game.View.Engine.EstimateMemoryUsage();
                    using (var tCount = new NString(string.Format("{0}/{1}", liveCount, capacity)))
                        Nuke.nk_text(ctx, tCount.pText, tCount.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                    using (var tMemory = new NString(string.Format("{0:0000.00}MB", memory / (1024 * 1024.0))))
                        Nuke.nk_text(ctx, tMemory.pText, tMemory.Length, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_RIGHT);
                }
            }

            // }
        }

        private unsafe void RenderDocumentProperties () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Document", "Document", false))
            if (group.Visible && (Game.View != null)) {
                var data = Game.View.GetData();
                DocumentProperties.Prepare(data);
                RenderPropertyGrid(data, DocumentProperties, null);
            }
        }

        private unsafe void RenderVariableList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Variables", "Variables", false))
            if (group.Visible) {
                Nuklear.NewRow(LineHeight, 2);
                if (Nuklear.Button("Add"))
                    Controller.AddVariable();
                if (Nuklear.Button("Remove", Model.NamedVariables.Count > 0))
                    Controller.RemoveVariable(Controller.SelectedVariableName);

                using (var list = Nuklear.ScrollingGroup(120, "Variable List", ref state.Variables.ScrollX, ref state.Variables.ScrollY))
                if (list.Visible) {
                    Nuklear.NewRow(LineHeight, 1);
                    var names = Model.NamedVariables.Keys.ToArray();
                    for (int i = 0; i < names.Length; i++) {
                        var name = names[i];
                        if (Nuklear.SelectableText(
                            string.IsNullOrWhiteSpace(name) ? string.Format("{0} Unnamed", i) : name, 
                            Controller.SelectedVariableName == name
                        ))
                            Controller.SelectedVariableName = name;
                    }
                }
            }

            var n = Controller.SelectedVariableName;
            var def = Controller.SelectedVariableDefinition;
            if (def == null)
                return;

            bool changed = false;

            if (Game.View == null)
                return;

            using (var group = Nuklear.CollapsingGroup("Variable " + n, "Variable", true))
            if (group.Visible) {
                Nuklear.NewRow(LineHeight, 1);
                string newName = n;
                if (Nuklear.Textbox(ref newName, tooltip: "Variable Name")) {
                    if (Controller.RenameVariable(n, newName))
                        n = newName;
                    else
                        newName = n;
                }

                Nuklear.NewRow(LineHeight, 2);

                var currentTypeName = def.ValueType.Name;
                var currentTypeIndex = Array.IndexOf(ValidVariableTypeNames, currentTypeName);
                if (Nuklear.ComboBox(ref currentTypeIndex, (i) => (i < 0) ? "" : ValidVariableTypeNames[i], ValidVariableTypeNames.Length, "Variable Type")) {
                    var newType = ValidVariableTypes[currentTypeIndex];
                    if (newType != def.ValueType) {
                        var ptype = typeof(Squared.Illuminant.Configuration.Parameter<>).MakeGenericType(newType);
                        def.DefaultValue = (Squared.Illuminant.Configuration.IParameter)Activator.CreateInstance(ptype);
                        changed = true;
                    }
                }

                Nuklear.Checkbox("External Value", ref def.IsExternal, "If set, the value of this variable will be determined at runtime");

                var p = def.DefaultValue;

                NameStack.Clear();
                if (p.ValueType.Name.Contains("Matrix"))
                    NameStack.Push(newName);

                RenderParameter(null, Model.NamedVariables, ref changed, newName, null, ref p, false);
                if (changed)
                    def.DefaultValue = p;

                NameStack.Clear();
            }
        }

        private unsafe void RenderSystemList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Systems", "Systems", false))
            if (group.Visible) {
                Nuklear.NewRow(LineHeight, 2);
                if (Nuklear.Button("Add"))
                    Controller.AddSystem();
                if (Nuklear.Button("Remove", Model.Systems.Count > 0))
                    Controller.RemoveSystem(state.Systems.SelectedIndex);

                using (var list = Nuklear.ScrollingGroup(90, "System List", ref state.Systems.ScrollX, ref state.Systems.ScrollY))
                if (list.Visible) {
                    Nuklear.NewRow(LineHeight, 1);
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

            Nuklear.NewRow(LineHeight + 3, 1);
            Nuklear.Textbox(ref s.Model.Name, "System Name");
            NameStack.Clear();
            NameStack.Push(s.Model.Name);

            Nuklear.NewRow(LineHeight + 3, 1);
            Nuklear.Textbox(ref s.Model.Tags, "Tags (comma separated)");

            Nuklear.NewRow(LineHeight + 3, 2);
            Nuklear.Property("#Draw Order", ref s.Model.DrawOrder, -1, Model.Systems.Count + 1, 1, 0.5f);
            Nuklear.Property("#Update Order", ref s.Model.UpdateOrder, -1, Model.Systems.Count + 1, 1, 0.5f);
            bool addBlend = s.Model.AdditiveBlend;
            Nuklear.Checkbox("Additive Blend", ref addBlend);
            s.Model.AdditiveBlend = addBlend;

            var config = Controller.SelectedSystem.Model.Configuration;
            SystemProperties.Prepare(config);
            RenderPropertyGrid(config, SystemProperties, null);
        }

        private unsafe void RenderTransformList () {
            var ctx = Nuklear.Context;
            var state = Controller.CurrentState;

            using (var group = Nuklear.CollapsingGroup("Transforms", "Transforms", true))
            if (group.Visible && (Controller.SelectedSystem != null)) {
                Nuklear.NewRow(LineHeight, 3);
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
                    Nuklear.NewRow(LineHeight, 1);
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
                Nuklear.NewRow(LineHeight + 3, 1);

                Nuklear.Textbox(ref xform.Model.Name, "Transform Name");
                NameStack.Clear();
                NameStack.Push(Controller.SelectedSystem.Model.Name);
                NameStack.Push(xform.Model.Name);

                Nuklear.NewRow(LineHeight + 3, 1);
                Nuklear.Textbox(ref xform.Model.Tags, "Tags (comma separated)");

                Nuklear.NewRow(LineHeight + 3, 2);
                int typeIndex = TransformTypes.IndexOf(xform.Model.Type);

                if (Nuklear.ComboBox(ref typeIndex, (i) => TransformTypes[i].Name, TransformTypes.Count, "Transform Type")) {
                    Controller.ChangeTransformType(xform, TransformTypes[typeIndex]);
                } else {
                    if (Nuklear.Property("#Update Order", ref xform.Model.UpdateOrder, -1, Controller.SelectedSystem.Transforms.Count + 1, 1, 0.5f))
                        TransformSortRequired = true;

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

                NameStack.Pop();
            }
        }

        private unsafe void RenderGlobalSettings () {
            var ctx = Nuklear.Context;

            using (var sGlobalSettings = new NString("Global Settings"))
            if (Nuke.nk_tree_push_hashed(ctx, NuklearDotNet.nk_tree_type.NK_TREE_TAB, sGlobalSettings.pText, NuklearDotNet.nk_collapse_states.NK_MINIMIZED, sGlobalSettings.pText, sGlobalSettings.Length, 256) != 0) {
                Nuklear.NewRow(LineHeight, 4);

                var vsync = Graphics.SynchronizeWithVerticalRetrace;
                if (Nuklear.Checkbox("VSync", ref vsync)) {
                    Graphics.SynchronizeWithVerticalRetrace = vsync;
                    Graphics.ApplyChangesAfterPresent(Game.RenderCoordinator);
                }

                var fullscreen = Graphics.IsFullScreen;
                if (Nuklear.Checkbox("Fullscreen", ref fullscreen))
                    Game.SetFullScreen(fullscreen);

                var msaa = Graphics.PreferMultiSampling;
                if (Nuklear.Checkbox("MSAA", ref msaa)) {
                    Graphics.PreferMultiSampling = msaa;
                    Graphics.ApplyChangesAfterPresent(Game.RenderCoordinator);
                }

                Nuklear.Checkbox("Stats", ref Game.ShowPerformanceStats);

                Nuklear.NewRow(LineHeight, 2);

                Nuklear.Property("Zoom", ref Game.Zoom, EditorGame.MinZoom, EditorGame.MaxZoom, 0.05f, 0.01f);

                Nuke.nk_tree_pop(ctx);
            }
        }
    }
}
