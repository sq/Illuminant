﻿using System;
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
            return result;
        }

        public void Initialize (EditorGame editor) {
            Game = editor;
            base.Initialize(Game.RenderCoordinator, Game.Materials, Game.ParticleMaterials);
            Engine.ChangeChunkSizeAndReset((int)GetData().ChunkSize);
            // Engine.Configuration.UpdatesPerSecond = 120;
        }
        
        public void Update (EditorGame editor, IBatchContainer container, int layer, long deltaTimeTicks) {
            var doUpdate = !editor.Controller.Paused || editor.Controller.StepPending;
            if (!doUpdate)
                return;

            foreach (var system in Systems)
                system.Instance.Configuration.AutoReadback = GetData().DrawAsBitmaps && (system.Model.Configuration.Appearance?.Texture?.IsInitialized ?? false);

            editor.Controller.StepPending = false;
            Time.Advance(deltaTimeTicks);

            base.Update(container, layer, deltaTimeTicks);
        }

        public void Draw (EditorGame editor, IBatchContainer container, int layer) {
            base.Draw(container, layer);
        }
    }
}
