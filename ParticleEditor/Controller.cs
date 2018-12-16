using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;

namespace ParticleEditor {
    public class Controller {
        public struct ListState {
            public uint ScrollX, ScrollY;
            public int SelectedIndex;
        }

        [StructLayout(LayoutKind.Sequential)]
        public class State {
            public ListState Systems, Transforms;
        }

        public Model Model;
        public View View;
        public readonly State CurrentState = new State();

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

        public Controller (Model model, View view) {
            Model = model;
            View = view;
            StatePin = GCHandle.Alloc(CurrentState, GCHandleType.Pinned);
        }

        public void AddSystem () {
            var config = new ParticleSystemConfiguration {
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

        public void AddTransform () {
            var view = SelectedSystem;
            var model = view.Model;
            var xformModel = new ParticleTransformModel {
                Type = typeof(Spawner),
                Properties = {
                    { "MinRate", 240 },
                    { "MaxRate", 240 },
                    { "Position",
                        new Formula {
                            Constant = new Vector4(256 * (view.Transforms.Count + 1), 256, 0, 256),
                            RandomScale = new Vector4(256, 256, 0, 0),
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0, 0),
                            RandomCircularity = 1
                        }
                    },
                    { "Velocity",
                        new Formula {
                            RandomScale = new Vector4(32f, 32f, 0, 0),
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0, 0),
                            RandomCircularity = 1
                        }
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
    }
}
