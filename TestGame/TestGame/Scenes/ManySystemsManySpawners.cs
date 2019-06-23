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

namespace TestGame.Scenes {
    public class SystemStress : Scene {
        ParticleEngine Engine;

        public List<ParticleSystem> Systems = new List<ParticleSystem>();

        public SystemStress (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        public override void LoadContent () {
            Engine = new ParticleEngine(
                Game.RenderCoordinator, Game.Materials, 
                new ParticleEngineConfiguration(32) {
                    TextureLoader = Game.TextureLoader.Load,
                    AccurateLivenessCounts = true
                }, Game.ParticleMaterials
            );

            var sconfig = new ParticleSystemConfiguration {};

            for (int i = 0; i < 256; i++) {
                var s = new ParticleSystem(Engine, sconfig);
                Systems.Add(s);
            }

            var random = new Random();
            AddSpawners(Systems[0], 16, random);
            AddSpawners(Systems[2], 64, random);
            AddSpawners(Systems[5], 10, random);
            AddSpawners(Systems[9], 12, random);
        }

        private void AddSpawners (ParticleSystem system, int count, Random random) {
            for (int i = 0; i < count; i++) {
                var spawner = new Spawner {
                    Position = {
                        Constant = new Vector3(random.NextFloat(0, Width), random.NextFloat(0, Height), 0),
                        RandomScale = new Vector3(16, 16, 0)
                    },
                    MinRate = 10,
                    MaxRate = 50
                };
                system.Transforms.Add(spawner);
            }
        }

        public override void UnloadContent () {
            Engine.Dispose();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.DarkTurquoise * 0.3f);

            using (var group = BatchGroup.New(
                frame, 2, (dm, _) => {
                    var vt = Game.Materials.ViewTransform;
                    Game.Materials.ViewTransform = vt;
                }
            )) {
                int i = -1, j = 1;
                foreach (var s in Systems) {
                    s.Update(frame, i--);
                    s.Render(group, j++);
                }
            }
        }

        public override void Update (GameTime gameTime) {
            Game.IsMouseVisible = true;
        }
    }
}