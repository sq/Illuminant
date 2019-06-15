using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;
using Nuke = NuklearDotNet.Nuklear;

#if compiled_model

namespace TestGame.Scenes {
    public class LoadCompiledModel : Scene {
        ParticleEngine Engine;

        Squared.Illuminant.Compiled.Bear Bear;

        public LoadCompiledModel (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        private void CreateRenderTargets () {
        }

        public override void LoadContent () {
            Engine = new ParticleEngine(
                Game.RenderCoordinator, Game.Materials, 
                new ParticleEngineConfiguration(128) {
                    TextureLoader = Game.TextureLoader.Load
                }, Game.ParticleMaterials
            );

            Bear = new Squared.Illuminant.Compiled.Bear(Engine);
        }

        public override void UnloadContent () {
            Bear.Dispose();
            Engine.Dispose();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            int layer = -4;
            Bear.Update(frame, layer);
            layer += 2;

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.DarkTurquoise * 0.3f);

            using (var group = BatchGroup.New(
                frame, 2, (dm, _) => {
                    var vt = Game.Materials.ViewTransform;
                    vt.Position = new Vector2(-Width / 2f, -Height / 2f) / 3f;
                    vt.Scale = Vector2.One * 3f;
                    Game.Materials.ViewTransform = vt;
                }
            )) {
                layer = 0;
                Bear.Render(group, layer);
                layer += 2;
            }
        }

        public override void Update (GameTime gameTime) {
            Game.IsMouseVisible = true;
        }
    }
}

#endif