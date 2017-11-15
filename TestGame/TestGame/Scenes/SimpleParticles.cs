using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Illuminant;
using Squared.Render;
using Squared.Util;

namespace TestGame.Scenes {
    public class SimpleParticles : Scene {
        ParticleEngine Engine;
        ParticleSystem System;

        bool Deterministic = true;



        public SimpleParticles (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        private void CreateRenderTargets () {
        }

        public override void LoadContent () {
            Engine = new ParticleEngine(
                Game.Content, Game.RenderCoordinator, Game.Materials, 
                new ParticleEngineConfiguration {                    
                }
            );
            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(40960)
            );
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.MidnightBlue);

            System.Update(frame, 1);

            System.Render(frame, 2);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.R))
                    Deterministic = !Deterministic;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                var time = gameTime.TotalGameTime.TotalSeconds;
                if (Deterministic)
                    time = 3.3;
            }
        }
    }
}
