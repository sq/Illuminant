using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Particles;
using Squared.Render;

namespace ParticleEditor {
    public class View {
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
            foreach (var systemModel in Model.Systems) {
                var system = new ParticleSystemView { Model = systemModel };
                Systems.Add(system);
                system.Initialize(this);
            }
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
                    s.Instance.Render(g, i++);
            }
        }
    }

    public class ParticleSystemView {
        public ParticleSystem Instance;
        public ParticleSystemConfiguration Model;

        public void Initialize (View view) {
            Instance = new ParticleSystem(
                view.Engine, Model
            );
        }
    }
}
