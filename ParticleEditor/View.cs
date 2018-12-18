using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;

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
                    TextureLoader = editor.Content.Load<Texture2D>
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

        public void Update (ParticleEditor editor, IBatchContainer container, int layer, long deltaTimeTicks) {
            using (var g = BatchGroup.New(container, layer)) {
                int i = 0;
                foreach (var s in Systems) {
                    s.Time.Advance(deltaTimeTicks);
                    s.Instance.Update(g, i++);
                }
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
        public MockTimeProvider Time;
        public readonly List<ParticleTransformView> Transforms = new List<ParticleTransformView>();

        public void Initialize (View view) {
            Time = new MockTimeProvider();
            Model.Configuration.TimeProvider = Time;

            Instance = new ParticleSystem(
                view.Engine, Model.Configuration
            );

            foreach (var transform in Model.Transforms)
                AddNewViewForModel(transform);
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

            CreateInstance();

            System.Instance.Transforms.Add(Instance);
        }

        private void CreateInstance () {
            Instance = (ParticleTransform)Activator.CreateInstance(Model.Type);
            foreach (var kvp in Model.Properties) {
                var m = Model.Type.GetMember(kvp.Key).FirstOrDefault();
                if (m == null) {
                    Console.WriteLine("No property named {0}", kvp.Key);
                    continue;
                }
                var prop = m as PropertyInfo;
                var field = m as FieldInfo;
                Type targetType = (prop != null) ? prop.PropertyType : field.FieldType;
                try {
                    object value;
                    var jObject = kvp.Value as JObject;
                    if (jObject != null) {
                        value = jObject.ToObject(targetType);
                    } else {
                        value = Convert.ChangeType(kvp.Value, targetType);
                    }
                    
                    if (prop != null)
                        prop.SetValue(Instance, value);
                    else if (field != null)
                        field.SetValue(Instance, value);
                    else
                        Console.WriteLine("Member {0} is not field or property", kvp.Key);
                } catch (InvalidCastException) {
                    Console.WriteLine("Could not convert property {0} to appropriate type ({1})", kvp.Key, targetType.Name);
                }
            }

            Instance.IsActive = true;
        }

        public void TypeChanged () {
            var index = System.Instance.Transforms.IndexOf(Instance);
            Instance.Dispose();
            Instance = null;

            CreateInstance();
            System.Instance.Transforms[index] = Instance;
        }

        public void Dispose () {
            System.Instance.Transforms.Remove(Instance);
            Instance.Dispose();
            Instance = null;
        }
    }
}
