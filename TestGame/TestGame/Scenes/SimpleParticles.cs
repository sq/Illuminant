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
using Squared.Illuminant.Transforms;
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Util;

namespace TestGame.Scenes {
    public class SimpleParticles : Scene {
        ParticleEngine Engine;
        ParticleSystem System;

        bool Running = true;

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
                new ParticleSystemConfiguration(4000000, 2048)
            ) {
                Transforms = {
                    new VelocityFMA {
                        Add = new Vector3(0.0015f, 0.0006f, 0),
                        Multiply = Vector3.One * 0.995f
                    }
                }
            };

            Reset();
        }

        public void Reset () {
            int seed = 0;

            System.Initialize(
                (buf) => {
                    Parallel.For(
                        0, buf.Length, 
                        () => new MersenneTwister(Interlocked.Increment(ref seed)), 
                        (i, pls, rng) => {
                            var a = rng.NextDouble(0, Math.PI * 2);
                            var x = Math.Sin(a);
                            var y = Math.Cos(a);
                            var r = rng.NextDouble(1, 140);

                            buf[i] = new Vector4(
                                (float)(900 + (r * x)),
                                (float)(550 + (r * y)),
                                0,
                                1024
                            );

                            return rng;
                        },
                        (rng) => {}
                    );
                },
                (buf) => {
                    const float maxSpeed = 3.5f;

                    Parallel.For(
                        0, buf.Length, 
                        () => new MersenneTwister(Interlocked.Increment(ref seed)), 
                        (i, pls, rng) => {
                            var v = rng.NextFloat(maxSpeed * 0.6f, maxSpeed);
                            var a = rng.NextDouble(0, Math.PI * 2);

                            buf[i] = new Vector4(
                                (float)Math.Sin(a) * v,
                                (float)Math.Cos(a) * v,
                                0, 0
                            );

                            return rng;
                        },
                        (rng) => {}
                    );
                }
            );
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            if (Running)
                System.Update(frame, -2);

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.Black);

            if (Running)
                System.Render(frame, 1, blendState: BlendState.Additive);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.Space))
                    Running = !Running;
                if (KeyWasPressed(Keys.R))
                    Reset();

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
            }
        }
    }
}
