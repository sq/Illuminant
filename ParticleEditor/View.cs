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

namespace ParticleEditor {
    public class View : ParticleEngineView {
        public ParticleEditor Game { get; private set; }
        new public MockTimeProvider Time { get; private set; }

        public View (EngineModel model) 
            : base (model, new MockTimeProvider()) {
            Time = (MockTimeProvider)base.Time;
        }

        public EditorData GetData () {
            var ud = Model.UserData["EditorData"];
            var jud = ud as JObject;
            if (jud != null) {
                ud = jud.ToObject<EditorData>();
                Model.UserData["EditorData"] = ud;
            }
            return (EditorData)ud;
        }

        public void Initialize (ParticleEditor editor) {
            Game = editor;
            base.Initialize(Game.RenderCoordinator, Game.Materials, Game.ParticleMaterials);
            // Engine.Configuration.UpdatesPerSecond = 120;
        }
        
        public void Update (ParticleEditor editor, IBatchContainer container, int layer, long deltaTimeTicks) {
            var doUpdate = !editor.Controller.Paused || editor.Controller.StepPending;
            if (!doUpdate)
                return;

            editor.Controller.StepPending = false;
            Time.Advance(deltaTimeTicks);

            base.Update(container, layer, deltaTimeTicks);
        }

        public void Draw (ParticleEditor editor, IBatchContainer container, int layer) {
            base.Draw(container, layer);
        }
    }
}
