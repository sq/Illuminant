using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
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
            var fireball = Game.Content.Load<Texture2D>("fireball");
            var fireballRect = fireball.BoundsFromRectangle(new Rectangle(0, 0, 34, 21));

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(
                    attributeCount: 1,
                    maximumCount: 100000,
                    particlesPerRow: 1024
                )
            ) {
                ParticleTexture = fireball,
                ParticleTextureRegion = fireballRect,
                ParticleSize = new Vector2(34, 21),
                Transforms = {
                    new VelocityFMA {
                        Multiply = Vector3.One * 0.9998f,
                    },
                    new Gravity {
                        Attractors = {
                            new Gravity.Attractor {
                                Radius = 70,
                                Strength = 200
                            },
                            new Gravity.Attractor {
                                Radius = 10,
                                Strength = 300
                            },
                            new Gravity.Attractor {
                                Radius = 150,
                                Strength = 800
                            },
                        }
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

            system.Initialize<Vector4>(
                (buf) => {
                    Parallel.For(
                        0, buf.Length, 
                        () => new MersenneTwister(Interlocked.Increment(ref seed)), 
                        (i, pls, rng) => {
                            var a = rng.NextDouble(0, Math.PI * 2);
                            var x = Math.Sin(a);
                            var y = Math.Cos(a);
                            var r = rng.NextDouble(1, 300);

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
                    const float maxSpeed = 0.7f;

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
                },
                (buf) => {
                    Parallel.For(
                        0, buf.Length, 
                        () => new MersenneTwister(Interlocked.Increment(ref seed)), 
                        (i, pls, rng) => {
                            buf[i] = new Vector4(
                                rng.NextFloat(0.1f, 1),
                                rng.NextFloat(0.1f, 1),
                                rng.NextFloat(0.1f, 1), 1
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
                System.Render(
                    frame, 1, 
                    material: Engine.ParticleMaterials.AttributeColor, 
                    blendState: BlendState.Additive
                );
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

                var grav = (Gravity)System.Transforms[1];

                grav.Attractors[0].Position = new Vector3(
                    (float)((Math.Sin(time / 8) * 500) + (sz.X / 2)),
                    (float)((Math.Cos(time / 8) * 500) + (sz.Y / 2)),
                    0
                );
                grav.Attractors[1].Position = new Vector3(
                    (float)((Math.Sin((time / 4) + 0.7) * 220) + (sz.X / 2)),
                    (float)((Math.Cos((time / 4) + 0.8) * 220) + (sz.Y / 2)),
                    0
                );
                grav.Attractors[2].Position = new Vector3(
                    (float)((Math.Sin((-time / 13) + 1.2) * 700) + (sz.X / 2)),
                    (float)((Math.Cos((-time / 13) + 3.6) * 700) + (sz.Y / 2)),
                    0
                );

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
            }
        }
    }
}
