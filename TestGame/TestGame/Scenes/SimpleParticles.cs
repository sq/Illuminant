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
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer LightingRenderer;

        ParticleEngine Engine;
        ParticleSystem System;

        bool Running = true;
        int RandomSeed = 152;

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

            SetupParticleSystem();

            SetupDistanceFieldObjects();

            System.OnDeviceReset += InitializeSystem;
            Reset();
        }

        void SetupParticleSystem () {
            var sz = new Vector3(Width, Height, 0);
            var fireball = Game.Content.Load<Texture2D>("fireball");
            var fireballRect = fireball.BoundsFromRectangle(new Rectangle(0, 0, 34, 21));

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(
                    attributeCount: 1,
                    maximumCount: 2000000,
                    particlesPerRow: 2048
                ) {
                    Texture = fireball,
                    TextureRegion = fireballRect,
                    Size = new Vector2(34, 21) * 0.45f,
                    AnimationRate = new Vector2(1 / 6f, 0),
                    RotationFromVelocity = true,
                    OpacityFromLife = 8192,
                    EscapeVelocity = 3f,
                    BounceVelocityMultiplier = 0.85f
                }
            ) {
                Transforms = {
                    new MatrixMultiply {
                        // HACK: If we just use a rotation matrix, the particle eventually loses energy and
                        //  stops moving entirely. :(
                        Velocity = Matrix.Identity * 1.001f * Matrix.CreateRotationZ(0.015f),
                    },
                    new Gravity {
                        Attractors = {
                            new Gravity.Attractor {
                                Radius = 70,
                                Strength = 350
                            },
                            new Gravity.Attractor {
                                Radius = 10,
                                Strength = 800
                            },
                            new Gravity.Attractor {
                                Radius = 150,
                                Strength = 1200
                            },
                        }
                    },
                }
            };
        }

        void SetupDistanceFieldObjects () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 256;

            DistanceField = new DistanceField(
                Game.RenderCoordinator, Width, Height, Environment.MaximumZ,
                3, 0.5f
            );

            LightingRenderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    64, 64, true
                ) {
                    RenderScale = Vector2.One,
                    DistanceFieldMinStepSize = 1f,
                    DistanceFieldLongStepFactor = 0.5f,
                    DistanceFieldOcclusionToOpacityPower = 0.7f,
                    DistanceFieldMaxConeRadius = 24,
                    DistanceFieldUpdateRate = 1,
                }
            ) {
                DistanceField = DistanceField
            };
        }

        public void Reset () {
            InitializeSystem(System);

            {
                const int tileSize = 32;
                int numTilesX = (Width / tileSize) + 1;
                int numTilesY = (Height / tileSize) + 1;

                /*
                RandomSeed += 1;
                Console.WriteLine(RandomSeed);
                */

                Environment.Obstructions.Clear();
                var rng = new Random(RandomSeed);
                for (var i = 0; i < 24; i++) {
                    int x = rng.Next(0, numTilesX), y = rng.Next(0, numTilesY);
                    float sz = rng.NextFloat(40f, 100f);
                    Environment.Obstructions.Add(LightObstruction.Ellipsoid(
                        new Vector3(x * tileSize, y * tileSize, 0),
                        new Vector3(sz, sz, 60f)
                    ));
                }

                Environment.Obstructions.Add(LightObstruction.Box(
                    new Vector3(0, -45, 0),
                    new Vector3(Width, 50, 60f)
                ));

                Environment.Obstructions.Add(LightObstruction.Box(
                    new Vector3(0, Height + 45, 0),
                    new Vector3(Width, 50, 60f)
                ));

                Environment.Obstructions.Add(LightObstruction.Box(
                    new Vector3(-45, 0, 0),
                    new Vector3(50, Height, 60f)
                ));

                Environment.Obstructions.Add(LightObstruction.Box(
                    new Vector3(Width + 45, 0, 0),
                    new Vector3(50, Height, 60f)
                ));

                DistanceField.Invalidate();
            }

            GC.Collect();
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
                            var r = rng.NextDouble(1, 800);
                            x = (900 + (r * x));
                            y = (550 + (r * y));
                            x = Arithmetic.Clamp(x, 10, Width - 10);
                            y = Arithmetic.Clamp(y, 10, Height - 10);

                            buf[i] = new Vector4(
                                (float)x, (float)y, 0,
                                rng.NextFloat(
                                    system.Configuration.OpacityFromLife * 0.33f, 
                                    system.Configuration.OpacityFromLife
                                )
                            );

                            return rng;
                        },
                        (rng) => {}
                    );
                },
                (buf) => {
                    const float maxSpeed = 3f;

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
                                rng.NextFloat(0.02f, 0.22f),
                                rng.NextFloat(0.07f, 0.24f),
                                rng.NextFloat(0.06f, 0.28f), 1
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
            
            // This lighting renderer's job is to generate a distance field for collisions
            LightingRenderer.UpdateFields(frame, -3);

            System.Configuration.DistanceField = LightingRenderer.DistanceField;

            if (Running)
                System.Update(frame, -2);

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.Black);

            if (Running)
                System.Render(
                    frame, 1, 
                    material: Engine.ParticleMaterials.AttributeColor, 
                    blendState: BlendState.Additive
                );

            var lightDir = new Vector3(-0.5f, 0.5f, -1f);
            lightDir.Normalize();

            LightingRenderer.VisualizeDistanceField(
                Bounds.FromPositionAndSize(Vector2.Zero, new Vector2(Width, Height)),
                Vector3.UnitZ,
                frame, 2,
                mode: VisualizationMode.Outlines,
                blendState: BlendState.AlphaBlend
            );
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.Space))
                    Running = !Running;
                if (KeyWasPressed(Keys.R))
                    Reset();

                var time = (float)Time.Seconds;

                var sz = new Vector3(Width, Height, 0);

                if (System.Transforms.Count > 1) {
                    var grav = (Gravity)System.Transforms[1];

                    grav.Attractors[0].Position = new Vector3(
                        (float)((Math.Sin(time / 6) * 500) + (sz.X / 2)),
                        (float)((Math.Cos(time / 6) * 500) + (sz.Y / 2)),
                        0
                    );
                    grav.Attractors[1].Position = new Vector3(
                        (float)((Math.Sin((time / 2) + 0.7) * 220) + (sz.X * 0.55f * Arithmetic.PulseSine(time / 8, 0.8f, 1.0f))),
                        (float)((Math.Cos((time / 2) + 0.8) * 220) + (sz.Y * 0.43f * Arithmetic.PulseSine(time / 8, 0.8f, 1.0f))),
                        0
                    );
                    grav.Attractors[2].Position = new Vector3(
                        (float)((-Math.Sin((time / 13) + 1.2) * 700) + (sz.X / 2)),
                        (float)((Math.Cos((time / 13) + 3.6) * 600) + (sz.Y * 0.55f)),
                        0
                    );
                }

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
            }
        }
    }
}
