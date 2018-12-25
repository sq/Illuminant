using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;

namespace ParticleEditor {
    public class View : IDisposable {
        public ParticleEditor Game;
        public ParticleEngine Engine;
        public EngineModel Model;
        public readonly List<ParticleSystemView> Systems = new List<ParticleSystemView>();

        private readonly List<IDisposable> LoadedResources = new List<IDisposable>();

        public View (EngineModel model) {
            Model = model;
        }

        public void Initialize (ParticleEditor editor) {
            foreach (var system in Systems) {
                if (system.Instance != null)
                    system.Instance.Dispose();
            }
            if (Engine != null)
                Engine.Dispose();

            Game = editor;

            Engine = new ParticleEngine(
                editor.Content, editor.RenderCoordinator, editor.Materials,
                new ParticleEngineConfiguration {
                    TextureLoader = (fn) => LoadTexture(fn, false),
                    FPTextureLoader = (fn) => LoadTexture(fn, true)
                },
                editor.ParticleMaterials
            );
            Systems.Clear();
            foreach (var systemModel in Model.Systems)
                AddNewViewForModel(systemModel);
        }

        internal Texture2D LoadTexture (string name, bool floatingPoint) {
            Texture2D result;

            if (File.Exists(name)) {
                using (var img = new Squared.Render.STB.Image(name, premultiply: true, asFloatingPoint: floatingPoint))
                    result = img.CreateTexture(Game.RenderCoordinator, !floatingPoint);
            } else {
                lock (Game.RenderCoordinator.CreateResourceLock)
                    result = Game.Content.Load<Texture2D>(name);
            }

            LoadedResources.Add(result);
            return result;
        }

        internal void AddNewViewForModel (SystemModel model) {
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
            foreach (var r in LoadedResources)
                Engine.Coordinator.DisposeResource(r);

            LoadedResources.Clear();

            foreach (var s in Systems)
                s.Dispose();

            Systems.Clear();
        }
    }

    public class ParticleSystemView : IDisposable {
        public ParticleSystem Instance;
        public SystemModel Model;
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

        public void AddNewViewForModel (TransformModel model) {
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
        public TransformModel Model;

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
                    Console.WriteLine("Type {0} has no property named {1}", Model.Type.Name, kvp.Key);
                    continue;
                }
                var prop = m as PropertyInfo;
                var field = m as FieldInfo;
                Type targetType = (prop != null) ? prop.PropertyType : field.FieldType;
                if (targetType.Name == "Nullable`1")
                    targetType = targetType.GetGenericArguments()[0];

                try {
                    object value = kvp.Value.Value;
                    var jObject = value as JObject;
                    if (jObject != null)
                        value = jObject.ToObject(kvp.Value.Type);

                    value = Convert.ChangeType(value, targetType);
                    
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
