using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json.Linq;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;

namespace Squared.Illuminant.Modeling {
    public abstract class ParticleEngineView : IDisposable {
        public RenderCoordinator Coordinator { get; private set; }
        public ParticleEngine Engine { get; private set; }
        public EngineModel Model { get; private set; }
        public ITimeProvider Time;
        public readonly List<ParticleSystemView> Systems = new List<ParticleSystemView>();

        public ParticleSystem.UpdateResult[] UpdateResults { get; private set; }

        private readonly List<IDisposable> LoadedResources = new List<IDisposable>();

        protected ParticleEngineView (EngineModel model, ITimeProvider time) {
            Model = model;
            Time = new MockTimeProvider();
        }

        protected abstract string ResolveFilename (string name);

        protected void Initialize (
            RenderCoordinator coordinator, 
            DefaultMaterialSet materials, 
            ParticleMaterials particleMaterials = null
        ) {
            foreach (var system in Systems) {
                if (system.Instance != null)
                    system.Instance.Dispose();
            }
            if (Engine != null)
                Engine.Dispose();

            Coordinator = coordinator;
            Engine = new ParticleEngine(
                coordinator, materials,
                MakeConfiguration(), particleMaterials
            );

            Systems.Clear();
            foreach (var systemModel in Model.Systems)
                AddNewViewForModel(systemModel);
        }

        protected virtual ParticleEngineConfiguration MakeConfiguration () {
            return new ParticleEngineConfiguration {
                TextureLoader = (fn) => LoadTexture(fn, false),
                FPTextureLoader = (fn) => LoadTexture(fn, true),
                SystemResolver = ResolveReference,
                NamedVariableResolver = (k) => {
                    Configuration.IParameter result;
                    NamedVariableDefinition def;
                    if (!Model.NamedVariables.TryGetValue(k, out def))
                        result = null;
                    else
                        result = def.DefaultValue;

                    return result;
                },
            };
        }

        public ParticleSystem ResolveReference (string name, int? index) {
            ParticleSystemView result = null;
            if (name != null)
                result = Systems.FirstOrDefault(s => s.Model.Name == name);
            if ((result == null) && index.HasValue) {
                var idx = index.Value;
                if ((idx >= 0) && (idx < Systems.Count))
                    result = Systems[idx];
            }

            return result?.Instance;
        }

        internal Texture2D LoadTexture (string name, bool floatingPoint) {
            Texture2D result;
            var path = ResolveFilename(name);

            if (File.Exists(path)) {
                using (var img = new Squared.Render.STB.Image(path, premultiply: true, asFloatingPoint: floatingPoint))
                    result = img.CreateTexture(Coordinator, !floatingPoint);
            } else {
                // HACK: Create placeholder texture
                lock (Coordinator.CreateResourceLock) {
                    result = new Texture2D(Coordinator.Device, 2, 2);
                    result.SetData(new [] {
                        Color.Black, Color.White, Color.Black, Color.White
                    });
                }
            }

            LoadedResources.Add(result);
            return result;
        }

        public void AddNewViewForModel (SystemModel model) {
            var system = new ParticleSystemView { Model = model };
            Systems.Add(system);
            system.Initialize(Engine, Time);
        }

        protected void Update (IBatchContainer container, int layer, long deltaTimeTicks) {
            if ((UpdateResults == null) || (UpdateResults.Length != Systems.Count))
                UpdateResults = new ParticleSystem.UpdateResult[Systems.Count];

            using (var g = BatchGroup.New(container, layer)) {
                int i = 0;

                foreach (var s in Systems) {
                    UpdateResults[i] = s.Instance.Update(g, (s.Model.UpdateOrder << 16) + i);
                    i++;
                }
            }
        }

        protected void Draw (IBatchContainer container, int layer) {
            using (var g = BatchGroup.New(container, layer)) {
                int i = 0;
                foreach (var s in Systems)
                    s.Instance.Render(g, (s.Model.DrawOrder << 16) + i++, blendState: s.Model.BlendState ?? BlendState.AlphaBlend);
            }
        }

        public virtual void Dispose () {
            foreach (var r in LoadedResources)
                Engine.Coordinator.DisposeResource(r);

            LoadedResources.Clear();

            foreach (var s in Systems)
                s.Dispose();

            Systems.Clear();
        }
    }

    public class ParticleSystemView : IDisposable {
        internal class TransformSorter : IComparer<ParticleTransformView> {
            public int Compare (ParticleTransformView x, ParticleTransformView y) {
                return (x.Model.UpdateOrder - y.Model.UpdateOrder);
            }
        }

        internal static readonly TransformSorter Comparer = new TransformSorter();

        public ParticleSystem Instance;
        public SystemModel Model;
        public readonly List<ParticleTransformView> Transforms = new List<ParticleTransformView>();

        public void Initialize (ParticleEngine engine, ITimeProvider time) {
            Model.Configuration.TimeProvider = time;

            Instance = new ParticleSystem(
                engine, Model.Configuration
            );

            foreach (var transform in Model.Transforms)
                AddNewViewForModel(transform);

            Sort();
        }

        public void Sort () {
            Transforms.Sort(Comparer);
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

                    var descA = TypeDescriptor.GetConverter(kvp.Value.Type);
                    var descB = TypeDescriptor.GetConverter(targetType);
                    if (
                        (descA != null) && 
                        descA.CanConvertTo(targetType) && 
                        descA.CanConvertFrom(kvp.Value.Type)
                    )
                        value = descA.ConvertTo(value, targetType);
                    else if (
                        (descB != null) && 
                        descB.CanConvertTo(targetType) && 
                        descB.CanConvertFrom(kvp.Value.Type)
                    )
                        value = descB.ConvertTo(value, targetType);
                    else
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
