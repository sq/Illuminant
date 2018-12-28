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
            public Func<Vector2?> Get;
            public Action<Vector2> Set;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ListState {
            public uint ScrollX, ScrollY;
            public int SelectedIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class State {
            public ListState Systems, Transforms;
        }

        public readonly ParticleEditor Game;
        public EngineModel Model;
        public View View;
        public readonly State CurrentState = new State();
        public readonly List<ParticleSystemView> QueuedResets = new List<ParticleSystemView>();

        internal PositionPropertyInfo SelectedPositionProperty;

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
                return SelectedSystem.Transforms[CurrentState.Transforms.SelectedIndex];
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
                Size = Vector2.One * 1.5f
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
                    SelectedPositionProperty = null;

                if ((SelectedPositionProperty != null) && Game.LeftMouse) {
                    var pos = GetMouseWorldPosition();
                    SelectedPositionProperty.Set(pos);
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
            if (SelectedPositionProperty != null) {
                var scale = 0.85f;
                Vector2 drawPos;

                var currentPos = SelectedPositionProperty.Get();
                if (currentPos.HasValue) {
                    drawPos = currentPos.Value;
                    drawPos.X += 4;
                    drawPos.Y -= Game.UI.LineHeight * scale;
                    ir.DrawString(Game.Font, SelectedPositionProperty.Key, drawPos, material: Game.WorldSpaceTextMaterial, scale: scale);
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
                    { "Position",
                        ModelProperty.New(new Formula {
                            Constant = new Vector4(0, 0, 0, 64),
                            RandomScale = new Vector4(256, 256, 0, 0),
                            Circular = true
                        })
                    },
                    { "Velocity",
                        ModelProperty.New(new Formula {
                            RandomScale = new Vector4(32f, 32f, 0, 0),
                            Circular = true
                        })
                    },
                    { "Attributes",
                        ModelProperty.New(Formula.One())
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
