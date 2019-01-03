﻿using System;
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
using Squared.Illuminant;
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace ParticleEditor {
    public class Controller {
        internal class PositionPropertyInfo {
            public object Instance;
            public string Key;
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
        }

        public bool StepPending;
        public bool Paused;
        public readonly ParticleEditor Game;
        public EngineModel Model;
        public View View;
        public readonly State CurrentState = new State();
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

        public Squared.Illuminant.Configuration.IParameter SelectedVariable {
            get {
                Squared.Illuminant.Configuration.IParameter c;
                if ((SelectedVariableName == null) ||
                    !Model.NamedVariables.TryGetValue(SelectedVariableName, out c))
                    c = null;

                return c;
            }
        }

        public Controller (ParticleEditor game, EngineModel model, View view) {
            Game = game;
            Model = model;
            View = view;
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
            Model.NamedVariables.Add(name, value);
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

            Squared.Illuminant.Configuration.IParameter c;
            if (!Model.NamedVariables.TryGetValue(from, out c))
                return false;
            if (Model.NamedVariables.ContainsKey(to))
                return false;

            Model.NamedVariables.Remove(from);
            Model.NamedVariables.Add(to, c);

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
                v.Instance.Clear();

            QueuedResets.Clear();

            if (Game.IsActive && !Game.IsMouseOverUI) {
                var wheelDelta = Game.MouseState.ScrollWheelValue - Game.PreviousMouseState.ScrollWheelValue;
                var zoomDelta = wheelDelta * 0.1f / 160;
                var newZoom = Arithmetic.Clamp((float)Math.Round(Game.Zoom + zoomDelta, 2), ParticleEditor.MinZoom, ParticleEditor.MaxZoom);
                Game.Zoom = newZoom;

                if (Game.RightMouse)
                    SelectedProperty = null;

                if ((SelectedProperty != null) && Game.LeftMouse) {
                    var pos = GetMouseWorldPosition();
                    SelectedProperty.NewValue = pos;
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

        public void Draw (ParticleEditor editor, IBatchContainer container, int layer) {
            var ir = new ImperativeRenderer(
                container, Game.Materials, layer, blendState: BlendState.AlphaBlend, worldSpace: false
            );
            if (SelectedProperty != null) {
                var scale = 0.85f;
                Vector2 drawPos;

                var currentPos = SelectedProperty.CurrentValue;
                if (currentPos.HasValue) {
                    drawPos = currentPos.Value;
                    drawPos.X += 4;
                    drawPos.Y -= Game.UI.LineHeight * scale;
                    ir.DrawString(Game.Font, SelectedProperty.Key, drawPos, material: Game.WorldSpaceTextMaterial, scale: scale);
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
                    { "Attributes",
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
                "Particle Systems|*.particlesystem|All Files|*.*";
            dlg.DefaultExt = ".particlesystem";
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
                }
            });
        }

        public void SetModel (EngineModel model) {
            model.Normalize(false);
            Game.Model = model;
            Model = model;
            Game.RenderCoordinator.DisposeResource(View);
            Game.View = View = new View(model);
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
