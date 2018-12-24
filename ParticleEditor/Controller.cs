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
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;

namespace ParticleEditor {
    public class Controller {
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
        public Model Model;
        public View View;
        public readonly State CurrentState = new State();
        public readonly List<ParticleSystemView> QueuedResets = new List<ParticleSystemView>();

        private GCHandle StatePin;

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

        public Controller (ParticleEditor game, Model model, View view) {
            Game = game;
            Model = model;
            View = view;
            StatePin = GCHandle.Alloc(CurrentState, GCHandleType.Pinned);
        }

        public void AddSystem () {
            var config = new ParticleSystemConfiguration(1) {
                OpacityFromLife = 256,
                GlobalLifeDecayRate = 90,
                Size = Vector2.One * 1.5f
            };
            var model = new ParticleSystemModel {
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

        public void QueueReset (ParticleSystemView v) {
            QueuedResets.Add(v);
        }

        public void Update () {
            foreach (var v in QueuedResets) {
                v.Time.CurrentTime = 0;
                v.Instance.Clear();
            }

            QueuedResets.Clear();
        }

        public void AddTransform () {
            var view = SelectedSystem;
            var model = view.Model;
            var xformModel = new ParticleTransformModel {
                Type = typeof(Spawner),
                Properties = {
                    { "MinRate", ModelProperty.New(240) },
                    { "MaxRate", ModelProperty.New(240) },
                    { "Position",
                        ModelProperty.New(new Formula {
                            Constant = new Vector4(0, 0, 0, 256),
                            RandomScale = new Vector4(256, 256, 0, 0),
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0, 0),
                            Circular = true
                        })
                    },
                    { "Velocity",
                        ModelProperty.New(new Formula {
                            RandomScale = new Vector4(32f, 32f, 0, 0),
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0, 0),
                            Circular = true
                        })
                    },
                    { "Attributes",
                        ModelProperty.New(new Formula {
                            Constant = Vector4.One
                        })
                    }
                }
            };
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
            Game.RunWorkItem(() => {
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
            Game.RunWorkItem(() => {
                using (var dlg = new OpenFileDialog {
                    Title = "Load"
                }) {
                    InitSystemDialog(dlg);
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;

                    var model = Model.Load(dlg.FileName);
                    if (model == null)
                        Console.WriteLine("Failed to load file");
                    else
                        SetModel(model);
                }
            });
        }

        public void SetModel (Model model) {
            model.Normalize();
            Game.Model = model;
            Model = model;
            Game.RenderCoordinator.DisposeResource(View);
            Game.View = View = new View(model);
            View.Initialize(Game);
        }

        internal void SelectTexture (ParticleEditor.CachedPropertyInfo cpi, object instance, ParticleTexture pt) {
            Game.RunWorkItem(() => {
                using (var dlg = new OpenFileDialog {
                    Title = "Select Texture"
                }) {
                    InitFileDialog(dlg);
                    dlg.Filter =
                        "Textures|*.png;*.jpeg;*.jpg;*.bmp;*.tga|All Files|*.*";
                    if (pt.Texture.Name != null) {
                        dlg.InitialDirectory = Path.GetDirectoryName(pt.Texture.Name);
                        dlg.RestoreDirectory = false;
                        dlg.FileName = Path.GetFileName(pt.Texture.Name);
                    }
                    if (dlg.ShowDialog() != DialogResult.OK)
                        return;

                    pt.Texture = new NullableLazyResource<Texture2D>(dlg.FileName);
                    cpi.Setter(instance, pt);
                }
            });
        }
    }

    public class XnaJsonConverter : JsonConverter {
        public override bool CanConvert (Type objectType) {
            switch (objectType.Name) {
                case "ModelProperty":
                case "Matrix":
                    return true;
                default:
                    return false;
            }
        }

        public override object ReadJson (JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
            switch (objectType.Name) {
                case "ModelProperty":
                    var obj = JObject.Load(reader);
                    var type = Type.GetType(obj["Type"].ToString(), true, false);
                    var result = new ModelProperty {
                        Type = type,
                        Value = obj["Value"].ToObject(type, serializer)
                    };
                    return result;
                case "Matrix":
                    var arr = serializer.Deserialize<float[]>(reader);
                    return new Matrix(
                        arr[0], arr[1], arr[2], arr[3],
                        arr[4], arr[5], arr[6], arr[7],
                        arr[8], arr[9], arr[10], arr[11],
                        arr[12], arr[13], arr[14], arr[15]
                    );
                default:
                    throw new NotImplementedException();
            }
        }

        public override void WriteJson (JsonWriter writer, object value, JsonSerializer serializer) {
            if (value == null)
                return;

            var type = value.GetType();
            switch (type.Name) {
                case "ModelProperty":
                    var mp = (ModelProperty)value;
                    serializer.Serialize(writer, new {
                        Type = mp.Type,
                        Value = mp.Value
                    });
                    return;
                case "Matrix":
                    var m = (Matrix)value;
                    var values = new float[] {
                        m.M11, m.M12, m.M13, m.M14,
                        m.M21, m.M22, m.M23, m.M24,
                        m.M31, m.M32, m.M33, m.M34,
                        m.M41, m.M42, m.M43, m.M44,
                    };
                    serializer.Serialize(writer, values);
                    return;
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
