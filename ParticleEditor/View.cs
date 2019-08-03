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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Squared.Illuminant.Modeling;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Util;

namespace Lumined {
    public class View : ParticleEngineView {
        public EditorGame Game { get; private set; }
        new public MockTimeProvider Time { get; private set; }

        public View (EngineModel model) 
            : base (model, new MockTimeProvider()) {
            Time = (MockTimeProvider)base.Time;
        }

        public EditorData GetData () {
            return Model.GetUserData<EditorData>("EditorData");
        }

        protected override ParticleEngineConfiguration MakeConfiguration () {
            var result = base.MakeConfiguration();
            result.AccurateLivenessCounts = GetData()?.AccurateCounting ?? true;
            return result;
        }

        protected override string ResolveFilename (string name) {
            var basePath = Path.GetFullPath(Model.GetUserData<EditorData>("EditorData")?.ResourceDirectory?.Path ?? ".");
            return Path.Combine(basePath, name);
        }

        public void Initialize (EditorGame editor) {
            Game = editor;
            base.Initialize(Game.RenderCoordinator, Game.Materials, Game.ParticleMaterials);
            Engine.ChangePropertiesAndReset((int)GetData().ChunkSize);
            // Engine.Configuration.UpdatesPerSecond = 120;
        }
        
        public void Update (EditorGame editor, IBatchContainer container, int layer, long deltaTimeTicks) {
            var doUpdate = (!editor.Controller.Paused || editor.Controller.StepPending);
            if (!doUpdate)
                return;

            if (Time.Ticks >= 0) {
                foreach (var system in Systems)
                    system.Instance.Configuration.AutoReadback = GetData().DrawAsBitmaps && (system.Model.Configuration.Appearance?.Texture?.IsInitialized ?? false);

                editor.Controller.StepPending = false;

                base.Update(container, layer, deltaTimeTicks);
            }

            if (editor.View.GetData().FixedTimeStep)
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0).Ticks);
            else
                Time.Advance(deltaTimeTicks);
        }

        public void Draw (EditorGame editor, IBatchContainer container, int layer) {
            base.Draw(container, layer);
        }
    }
}
