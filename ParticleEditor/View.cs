﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;

namespace ParticleEditor {
    public class View : IDisposable {
        public ParticleEngine Engine;
        public Model Model;
        public readonly List<ParticleSystemView> Systems = new List<ParticleSystemView>();

        public View (Model model) {
            Model = model;
        }

        public void Initialize (ParticleEditor editor) {
            foreach (var system in Systems) {
                if (system.Instance != null)
                    system.Instance.Dispose();
            }
            if (Engine != null)
                Engine.Dispose();

            Engine = new ParticleEngine(
                editor.Content, editor.RenderCoordinator, editor.Materials,
                new ParticleEngineConfiguration {
                    TextureLoader = editor.Content.Load<Texture2D>,
                    TimeProvider = editor.Time
                },
                editor.ParticleMaterials
            );
            Systems.Clear();
            foreach (var systemModel in Model.Systems)
                AddNewViewForModel(systemModel);
        }

        internal void AddNewViewForModel (ParticleSystemModel model) {
            var system = new ParticleSystemView { Model = model };
            Systems.Add(system);
            system.Initialize(this);
        }

        public void Update (ParticleEditor editor, IBatchContainer container, int layer, float? deltaTimeSeconds) {
            using (var g = BatchGroup.New(container, layer)) {
                int i = 0;
                foreach (var s in Systems)
                    s.Instance.Update(g, i++, deltaTimeSeconds);
            }
        }

        public void Draw (ParticleEditor editor, IBatchContainer container, int layer) {
            using (var g = BatchGroup.New(container, layer)) {
                int i = 0;
                foreach (var s in Systems)
                    s.Instance.Render(g, i++, blendState: BlendState.AlphaBlend);
            }
        }

        public void Dispose () {
            foreach (var s in Systems)
                s.Dispose();

            Systems.Clear();
        }
    }

    public class ParticleSystemView : IDisposable {
        public ParticleSystem Instance;
        public ParticleSystemModel Model;
        public readonly List<ParticleTransformView> Transforms = new List<ParticleTransformView>();

        public void Initialize (View view) {
            Instance = new ParticleSystem(
                view.Engine, Model.Configuration
            );

            foreach (var transform in Model.Transforms) {
                var transformView = new ParticleTransformView {
                    Model = transform
                };
                transformView.Initialize(this);
                Instance.Transforms.Add(transformView.Instance);
            }
        }

        public void AddNewViewForModel (ParticleTransformModel model) {
            var xform = new ParticleTransformView { Model = model };
            Transforms.Add(xform);
            xform.Initialize(this);
        }

        public void Dispose () {
            foreach (var transform in Transforms)
                transform.Dispose();

            Instance.Dispose();
            Transforms.Clear();
            Instance = null;
        }
    }

    public class ParticleTransformView : IDisposable {
        public ParticleSystemView System;
        public ParticleTransform Instance;
        public ParticleTransformModel Model;

        public void Initialize (ParticleSystemView view) {
            System = view;
            Instance = (ParticleTransform)Activator.CreateInstance(Model.Type);
            foreach (var kvp in Model.Properties) {
                var m = Model.Type.GetMember(kvp.Key).FirstOrDefault();
                if (m == null) {
                    Console.WriteLine("No property named {0}", kvp.Key);
                    continue;
                }
                var prop = m as PropertyInfo;
                var field = m as FieldInfo;
                if (prop != null)
                    prop.SetValue(Instance, kvp.Value);
                else if (field != null)
                    field.SetValue(Instance, kvp.Value);
                else
                    Console.WriteLine("Member {0} is not field or property", kvp.Key);
            }

            Instance.IsActive = true;

            System.Instance.Transforms.Add(Instance);
        }

        public void Dispose () {
            System.Instance.Transforms.Remove(Instance);
            Instance.Dispose();
            Instance = null;
        }
    }
}
