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

            var sz = new Vector3(Width, Height, 0);

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(4000000, 2048)
            ) {
                Transforms = {
                    /*
                    new VelocityFMA {
                        Add = new Vector3(0.0025f, 0.0016f, 0),
                        Multiply = Vector3.One * 0.992f,
                        AreaType = AreaType.Ellipsoid,
                        AreaCenter = sz * new Vector3(0.2f, 0.5f, 0),
                        AreaSize = new Vector3(300, 300, 1)
                    },
                    new VelocityFMA {
                        Add = new Vector3(-0.0015f, -0.0006f, 0),
                        Multiply = Vector3.One * 0.990f,
                        AreaType = AreaType.Ellipsoid,
                        AreaCenter = sz * new Vector3(0.8f, 0.5f, 0),
                        AreaSize = new Vector3(300, 300, 1)
                    },
                    new VelocityFMA {
                        Multiply = Vector3.One * 1.05f,
                        AreaType = AreaType.Ellipsoid,
                        AreaCenter = sz * new Vector3(0.5f, 0.1f, 0),
                        AreaSize = new Vector3(200, 200, 1)
                    },
                    new VelocityFMA {
                        Multiply = Vector3.One * 0.33f,
                        AreaType = AreaType.Ellipsoid,
                        AreaCenter = sz * new Vector3(0.5f, 0.9f, 0),
                        AreaSize = new Vector3(100, 100, 1)
                    },
                    */
                    new VelocityFMA {
                        Multiply = Vector3.One * 0.99995f,
                    },
                    new Gravity {
                        Radius = 100,
                        Strength = 300
                    },
                    new Gravity {
                        Radius = 10,
                        Strength = 400
                    },
                    new Gravity {
                        Radius = 200,
                        Strength = 1200
                    },
                }
            };

            System.OnDeviceReset += InitializeSystem;
            Reset();
        }

        public void Reset () {
            InitializeSystem(System);
        }

        private void InitializeSystem (ParticleSystem system) {
            int seed = 0;

            system.Initialize(
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
                    const float maxSpeed = 1.9f;

                    Parallel.For(
                        0, buf.Length, 
                        () => new MersenneTwister(Interlocked.Increment(ref seed)), 
                        (i, pls, rng) => {
                            var v = rng.NextFloat(maxSpeed * 0.25f, maxSpeed);
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

                var time = Time.Seconds;

                var sz = new Vector3(Width, Height, 0);

                ((Gravity)System.Transforms[1]).Position = new Vector3(
                    (float)((Math.Sin(time / 8) * 500) + (sz.X / 2)),
                    (float)((Math.Cos(time / 8) * 500) + (sz.Y / 2)),
                    0
                );
                ((Gravity)System.Transforms[2]).Position = new Vector3(
                    (float)((Math.Sin((time / 4) + 0.7) * 320) + (sz.X / 2)),
                    (float)((Math.Cos((time / 4) + 0.8) * 320) + (sz.Y / 2)),
                    0
                );
                ((Gravity)System.Transforms[3]).Position = new Vector3(
                    (float)((Math.Sin((time / 13) + 1.2) * 600) + (sz.X / 2)),
                    (float)((Math.Cos((time / 13) + 3.6) * 600) + (sz.Y / 2)),
                    0
                );

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
            }
        }
    }
}
