using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace Lumined {
    public class Controller {
        internal class PositionPropertyInfo {
            public object Instance;
            public string Key, DisplayName;
            public Vector2? CurrentValue;
            public Vector2? NewValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ListState {
            public uint ScrollX, ScrollY;
            public int SelectedIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class State {
            public ListState Systems, Transforms, Variables;

            public State Clone () {
                return (State)this.MemberwiseClone();
            }
        }

        public bool StepPending;
        public bool Paused;
        public readonly EditorGame Game;
        public EngineModel Model;
        public View View;
        public State CurrentState { get; private set; }
        public readonly List<ParticleSystemView> QueuedResets = new List<ParticleSystemView>();

        internal PositionPropertyInfo SelectedProperty = null, NewSelectedProperty = null;
        internal string SelectedVariableName;

        private int NextConstantID = 1;
        private GCHandle StatePin;

        public PropertyEditor UI {
            get {
                return Game.UI;
            }
        }

        public ParticleSystemView SelectedSystem {
            get {
                if (View.Systems.Count == 0)
                    return null;
                if (CurrentState.Systems.SelectedIndex >= View.Systems.Count)
                    return null;
                if (CurrentState.Systems.SelectedIndex < 0)
                    return null;
                return View.Systems[CurrentState.Systems.SelectedIndex];
            }
        }

        public ParticleTransformView SelectedTransform {
            get {
                if (SelectedSystem == null)
                    return null;
                if (SelectedSystem.Transforms.Count == 0)
                    return null;
                if (CurrentState.Transforms.SelectedIndex >= SelectedSystem.Transforms.Count)
                    return null;
                if (CurrentState.Transforms.SelectedIndex < 0)
                    return null;
                return SelectedSystem.Transforms[CurrentState.Transforms.SelectedIndex];
            }
        }

        public NamedVariableDefinition SelectedVariableDefinition {
            get {
                NamedVariableDefinition def;
                if ((SelectedVariableName == null) ||
                    !Model.NamedVariables.TryGetValue(SelectedVariableName, out def))
                    def = null;

                return def;
            }
        }

        public Squared.Illuminant.Configuration.IParameter SelectedVariable {
            get {
                return SelectedVariableDefinition?.DefaultValue;
            }
        }

        public Controller (EditorGame game, EngineModel model, View view) {
            Game = game;
            Model = model;
            View = view;
            CurrentState = new State();
            StatePin = GCHandle.Alloc(CurrentState, GCHandleType.Pinned);
        }

        public void AddSystem () {
            var config = new ParticleSystemConfiguration() {
                Color = {
                    OpacityFromLife = 64,
                },
                LifeDecayPerSecond = 64,
                Size = Vector2.One
            };
            var model = new SystemModel {
                Configuration = config
            };
            Model.Systems.Add(model);
            if (View.Engine != null)
                View.AddNewViewForModel(model);
        }

        public void RemoveSystem (int index) {
            var model = Model.Systems[index];
            var view = View.Systems[index];
            view.Dispose();
            Model.Systems.RemoveAt(index);
            View.Systems.RemoveAt(index);
        }

        public string AddVariable (Type valueType = null, string hintName = null) {
            if (valueType == null)
                valueType = typeof(Vector4);
            string name = hintName;
            if ((name == null) || Model.NamedVariables.ContainsKey(name))
                name = string.Format("var{0}", NextConstantID++);
            var tParameter = typeof(Squared.Illuminant.Configuration.Parameter<>).MakeGenericType(valueType);
            var value = (Squared.Illuminant.Configuration.IParameter)Activator.CreateInstance(tParameter);
            Model.NamedVariables.Add(name, new NamedVariableDefinition { DefaultValue = value });
            SelectedVariableName = name;
            return name;
        }

        public bool RenameVariable (string from, string to) {
            if (from == to)
                return false;
            if ((from == null) || (to == null))
                return false;
            if (string.IsNullOrWhiteSpace(to))
                return false;

            NamedVariableDefinition def;
            if (!Model.NamedVariables.TryGetValue(from, out def))
                return false;
            if (Model.NamedVariables.ContainsKey(to))
                return false;

            Model.NamedVariables.Remove(from);
            Model.NamedVariables.Add(to, def);

            if (SelectedVariableName == from)
                SelectedVariableName = to;
            return true;
        }

        public void RemoveVariable (string name) {
            if (name == null)
                return;
            Model.NamedVariables.Remove(name);
        }

        public void Step () {
            StepPending = true;
        }

        public void QueueReset () {
            View.Time.CurrentTime = 0;
            foreach (var s in View.Systems)
                QueuedResets.Add(s);
        }

        public Vector2 GetMouseWorldPosition () {
            var pos = new Vector2(Game.MouseState.X, Game.MouseState.Y);
            pos *= 1.0f / Game.Zoom;
            pos += Game.ViewOffset;
            return pos;
        }

        public void Update () {
            foreach (var v in QueuedResets)
                v.Instance.Reset();

            QueuedResets.Clear();

            var data = View.GetData();
            if ((data != null) && (View.Engine != null)) {
                View.Engine.Configuration.UpdatesPerSecond = data.FrameRate;
                View.Engine.Configuration.MaximumUpdateDeltaTimeSeconds = data.MaximumDeltaTimeMS / 1000f;
                if ((int)data.ChunkSize != View.Engine.Configuration.ChunkSize)
                    View.Engine.ChangeChunkSizeAndReset((int)data.ChunkSize);
            }

            if (Game.IsActive && !Game.IsMouseOverUI) {
                var wheelDelta = Game.MouseState.ScrollWheelValue - Game.PreviousMouseState.ScrollWheelValue;
                var zoomDelta = wheelDelta * 0.1f / 160;
                var newZoom = Arithmetic.Clamp((float)Math.Round(Game.Zoom + zoomDelta, 2), EditorGame.MinZoom, EditorGame.MaxZoom);
                Game.Zoom = newZoom;

                if (Game.RightMouse)
                    SelectedProperty = null;

                if ((SelectedProperty != null) && Game.LeftMouse) {
                    var pos = GetMouseWorldPosition();
                    SelectedProperty.NewValue = pos;
                }
            }

            if (UI.TransformSortRequired) {
                UI.TransformSortRequired = false;
                var ss = SelectedSystem;
                var st = SelectedTransform;

                foreach (var s in View.Systems) {
                    s.Model.Sort();
                    s.Sort();

                    if (SelectedSystem != s)
                        continue;
                    var newIndex = s.Transforms.IndexOf(st);
                    if (newIndex >= 0)
                        CurrentState.Transforms.SelectedIndex = newIndex;
                }
            }
        }

        private void DrawCross (ref ImperativeRenderer ir, Vector2 pos, float alpha, float len) {
            var pos1 = pos + Vector2.One;
            ir.DrawLine(new Vector2(pos1.X - len, pos1.Y), new Vector2(pos1.X + len, pos1.Y), Color.Black * alpha, worldSpace: true);
            ir.DrawLine(new Vector2(pos1.X, pos1.Y - len), new Vector2(pos1.X, pos1.Y + len), Color.Black * alpha, worldSpace: true);
            ir.DrawLine(new Vector2(pos.X - len, pos.Y), new Vector2(pos.X + len, pos.Y), Color.White * alpha, worldSpace: true);
            ir.DrawLine(new Vector2(pos.X, pos.Y - len), new Vector2(pos.X, pos.Y + len), Color.White * alpha, worldSpace: true);
        }

        public void Draw (EditorGame editor, IBatchContainer container, int layer) {
            var ir = new ImperativeRenderer(
                container, Game.Materials, layer, blendState: BlendState.AlphaBlend, worldSpace: false
            );
            if (SelectedProperty != null) {
                var area = SelectedProperty.Instance as TransformArea;
                if ((area != null) && (area.Type != AreaType.None)) {
                    var now = 0; // FIXME
                    var obj = new LightObstruction(
                        (LightObstructionType)(((int)area.Type)-1),
                        area.Center.Evaluate(now, View.Engine.ResolveVector3),
                        area.Size.Evaluate(now, View.Engine.ResolveVector3)
                    );
                    var falloff = Math.Max(area.Falloff.Evaluate(now, View.Engine.ResolveSingle), 1f);
                    var padding = (falloff * 2f) + 1f;
                    var bounds = obj.Bounds3.XY.Expand(padding, padding);
                    Game.LightingRenderer.VisualizeDistanceField(
                        bounds, -Vector3.UnitZ, container, layer, obj, VisualizationMode.Silhouettes,
                        worldBounds: obj.Bounds3.Expand(padding, padding, 0f),
                        outlineSize: falloff, color: new Vector4(0.4f, 0.2f, 0.1f, 0.2f)
                    );
                }

                var scale = 0.85f;
                Vector2 drawPos;

                var currentPos = SelectedProperty.CurrentValue;
                if (currentPos.HasValue) {
                    drawPos = currentPos.Value;
                    drawPos.X += 4;
                    drawPos.Y -= Game.UI.LineHeight * scale;
                    ir.DrawString(Game.Font, SelectedProperty.DisplayName, drawPos, material: Game.WorldSpaceTextMaterial, scale: scale);
                    DrawCross(ref ir, currentPos.Value, 1.0f, 12);
                }

                var pos = GetMouseWorldPosition();
                DrawCross(ref ir, pos, 0.5f, 12);

                DrawCross(ref ir, Vector2.Zero, 0.33f, 4096);
            }
        }

        public void AddTransform () {
            var view = SelectedSystem;
            var model = view.Model;
            var xformModel = new TransformModel {
                Type = typeof(Spawner),
                Properties = {
                    { "MinRate", ModelProperty.New(120) },
                    { "MaxRate", ModelProperty.New(240) },
                    { "Life",
                        ModelProperty.New(new Formula1 { Constant = 64 })
                    },
                    { "Position",
                        ModelProperty.New(new Formula3 {
                            Constant = new Vector3(0, 0, 0),
                            RandomScale = new Vector3(256, 256, 0),
                            Type = FormulaType.Spherical
                        })
                    },
                    { "Velocity",
                        ModelProperty.New(new Formula3 {
                            RandomScale = new Vector3(32f, 32f, 0),
                            Type = FormulaType.Spherical
                        })
                    },
                    { "Color",
                        ModelProperty.New(Formula4.One())
                    }
                }
            };
            model.Transforms.Add(xformModel);
            view.AddNewViewForModel(xformModel);
        }

        public void DuplicateTransform (int index) {
            var view = SelectedSystem;
            var model = view.Model;
            var template = model.Transforms[index];
            var xformModel = template.Clone();
            model.Transforms.Add(xformModel);
            view.AddNewViewForModel(xformModel);
        }

        public void RemoveTransform (int index) {
            var view = SelectedSystem;
            var model = view.Model;
            view.Transforms[index].Dispose();
            view.Transforms.RemoveAt(index);
            model.Transforms.RemoveAt(index);
        }

        public void ChangeTransformType (ParticleTransformView xform, Type type) {
            var model = xform.Model;
            model.Type = type;
            xform.TypeChanged();
        }

        private void InitSystemDialog (FileDialog dlg) {
            InitFileDialog(dlg);
            dlg.Filter =
                "Lumined Documents|*.lumined;*.particlesystem|All Files|*.*";
            dlg.DefaultExt = ".lumined";
        }

        private void InitFileDialog (FileDialog dlg) {
            dlg.SupportMultiDottedExtensions = false;
            dlg.RestoreDirectory = true;
            dlg.ShowHelp = false;
        }

        public void ShowSaveDialog () {
            Game.UI.RunWorkItem(() => {
                using (var dlg = new SaveFileDialog {
                    Title = "Save",
                    CreatePrompt = false,
                    OverwritePrompt = false
                }) {
                    InitSystemDialog(dlg);
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;

                    Model.UserData["ControllerState"] = CurrentState.Clone();
                    Model.Save(dlg.FileName);
                }
            });
        }

        public void ShowLoadDialog () {
            Game.UI.RunWorkItem(() => {
                using (var dlg = new OpenFileDialog {
                    Title = "Load"
                }) {
                    InitSystemDialog(dlg);
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;

                    var model = EngineModel.Load(dlg.FileName);
                    if (model == null)
                        Console.WriteLine("Failed to load file");
                    else
                        SetModel(model);

                    View.Time.CurrentTime = -Time.SecondInTicks;
                }
            });
        }

        public void SetModel (EngineModel model) {
            if (!model.UserData.ContainsKey("EditorData"))
                model.UserData["EditorData"] = new EditorData();
            model.Normalize(false);
            Game.Model = model;
            Model = model;
            Game.RenderCoordinator.DisposeResource(View);
            Game.View = View = new View(model);

            if (StatePin.IsAllocated)
                StatePin.Free();

            if (model.UserData.ContainsKey("ControllerState")) {
                CurrentState = (model.GetUserData<State>("ControllerState") ?? new State()).Clone();
            } else {
                CurrentState = new State();
            }
            StatePin = GCHandle.Alloc(CurrentState, GCHandleType.Pinned);

            View.Initialize(Game);
        }

        internal void SelectTexture (CachedPropertyInfo cpi, object instance, NullableLazyResource<Texture2D> tex) {
            if (tex == null)
                tex = new NullableLazyResource<Texture2D>();

            Game.UI.RunWorkItem(() => {
                using (var dlg = new OpenFileDialog {
                    Title = "Select Texture"
                }) {
                    InitFileDialog(dlg);
                    dlg.Filter =
                        "Textures|*.png;*.jpeg;*.jpg;*.bmp;*.tga|All Files|*.*";
                    if (tex.Name != null) {
                        dlg.InitialDirectory = Path.GetDirectoryName(tex.Name);
                        dlg.RestoreDirectory = false;
                        dlg.FileName = Path.GetFileName(tex.Name);
                    }
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;

                    tex = new NullableLazyResource<Texture2D>(dlg.FileName);
                    cpi.Setter(instance, tex);
                }
            });
        }
    }
}
