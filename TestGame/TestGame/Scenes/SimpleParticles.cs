﻿using System;
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
    public class SimpleParticles : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer LightingRenderer;

        ParticleEngine Engine;
        ParticleSystem System;

        Toggle Running, ShowDistanceField, Collisions, SpawnFromTemplate;
        Slider EscapeVelocity, Friction, BounceVelocity;

        int RandomSeed = 201;

        const float ParticlesPerPixel = 2;
        const int   SpawnInterval     = 4;
        const int   SpawnCount        = 1024;
        const int   MaxLife           = 360;

        int SpawnOffset = 0;
        int FramesUntilNextSpawn = 0;

        Texture2D Pattern;
        Color[]   PatternPixels;

        public SimpleParticles (TestGame game, int width, int height)
            : base(game, width, height) {
            Running.Value = true;
            ShowDistanceField.Value = false;
            Collisions.Value = true;
            SpawnFromTemplate.Value = true;

            Running.Key = Keys.Space;
            ShowDistanceField.Key = Keys.D;
            Collisions.Key = Keys.C;
            SpawnFromTemplate.Key = Keys.T;

            EscapeVelocity.Max = 2048;
            EscapeVelocity.Value = 16 * 60f;
            EscapeVelocity.Speed = 16;

            Friction.Max = 3.0f;
            Friction.Value = 0.1f;
            Friction.Speed = 0.05f;

            BounceVelocity.Max = 2.0f;
            BounceVelocity.Value = 0.95f;
            BounceVelocity.Speed = 0.05f;
        }

        private void CreateRenderTargets () {
        }

        public override void LoadContent () {
            Engine = new ParticleEngine(
                Game.Content, Game.RenderCoordinator, Game.Materials, 
                new ParticleEngineConfiguration {
                    TextureLoader = Game.Content.Load<Texture2D>
                }, Game.ParticleMaterials
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
            var spark = Game.Content.Load<Texture2D>("spark");

            Pattern = Game.Content.Load<Texture2D>("template");

            var width = Pattern.Width;
            PatternPixels = new Color[width * Pattern.Height];
            Pattern.GetData(PatternPixels);

            const int opacityFromLife = 200;

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(
                    attributeCount: 1
                ) {
                    Texture = new NullableLazyResource<Texture2D>("spark"),
                    Size = Vector2.One * 2.6f,
                    /*
                    Texture = fireball,
                    TextureRegion = fireballRect,
                    Size = new Vector2(34, 21) * 0.2f,
                    AnimationRate = new Vector2(1 / 6f, 0),
                    */
                    RotationFromVelocity = true,
                    OpacityFromLife = opacityFromLife / 60f,
                    MaximumVelocity = 2048,
                    CollisionLifePenalty = 1,
                }
            ) {
                Transforms = {
                    new Spawner {
                        IsActive = false,
                        MinRate = 102400,
                        MaxRate = 409600,
                        Position = new Formula {
                            Constant = new Vector4(Pattern.Width / 2f, Pattern.Height / 2f, 0, (opacityFromLife) / 60f),
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0f, 0f),
                            RandomScale = new Vector4(900f * 2f, 450f * 2f, 0f, (MaxLife - opacityFromLife) / 60f),
                        },
                        Velocity = new Formula {
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0f, 0f),
                            RandomScale = new Vector4(60f, 60f, 0f, 0f),
                            RandomCircularity = 1f
                        },
                        Attributes = new Formula {
                            Constant = new Vector4(0.09f, 0.09f, 0.09f, 1f),
                            RandomScale = new Vector4(0.3f, 0.3f, 0.3f, 0f)
                        }
                    },
                    new Gravity {
                        Attractors = {
                            new Gravity.Attractor {
                                Radius = 150,
                            },
                            new Gravity.Attractor {
                                Radius = 10,
                            },
                            new Gravity.Attractor {
                                Radius = 400,
                            },
                            new Gravity.Attractor {
                                Radius = 40,
                            },
                        }
                    },
                    /*
                    new FMA {
                        Velocity = {
                            Multiply = Vector3.One * 0.9993f
                        }
                    },
                    */
                    /*
                    new FMA {
                        Velocity = {
                            Multiply = Vector3.One * 0.98f,
                            Add = new Vector3(-0.03f, -0.008f, 0f)
                        },
                        Area = new TransformArea(AreaType.Ellipsoid) {
                            Center = sz * 0.8f,
                            Size = Vector3.One * 125,
                            Falloff = 150
                        }
                    },
                    */
                    new MatrixMultiply {
                        Velocity = Matrix.CreateRotationZ((float)Math.PI * 0.002f) * Matrix.CreateScale(1f, 1f, 0f),
                        Position = Matrix.CreateScale(1f, 1f, 0f)
                    }
                }
            };
        }

        void SetupDistanceFieldObjects () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 64;

            DistanceField = new DistanceField(
                Game.RenderCoordinator, Width, Height, Environment.MaximumZ,
                9, 1 / 4f, maximumEncodedDistance: 320
            );

            LightingRenderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    64, 64, true
                ) {
                    RenderScale = Vector2.One,
                    MaximumFieldUpdatesPerFrame = 1,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    }
                }, Game.IlluminantMaterials
            ) {
                DistanceField = DistanceField
            };
        }

        public void Reset () {
            InitializeSystem(System);
            FramesUntilNextSpawn = 0;
            SpawnOffset = 0;

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
                for (var i = 0; i < 4; i++) {
                    int x = rng.Next(0, numTilesX), y = rng.Next(0, numTilesY);
                    float sz = rng.NextFloat(100f, 200f);
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
            system.Configuration.DistanceFieldMaximumZ = 256;
            system.Clear();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();
            
            // This lighting renderer's job is to generate a distance field for collisions
            LightingRenderer.UpdateFields(frame, -3);

            System.Configuration.DistanceField = Collisions ? LightingRenderer.DistanceField : null;
            System.Configuration.Friction = Friction;
            System.Configuration.EscapeVelocity = EscapeVelocity;
            System.Configuration.BounceVelocityMultiplier = BounceVelocity;

            if (Running)
                System.Update(frame, -2);

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.Black);

            // if (Running)
                System.Render(
                    frame, 2, 
                    material: Engine.ParticleMaterials.AttributeColor,
                    blendState: RenderStates.AdditiveBlend
                );

            var lightDir = new Vector3(-0.5f, 0.5f, -1f);
            lightDir.Normalize();

            if (ShowDistanceField)
                LightingRenderer.VisualizeDistanceField(
                    Bounds.FromPositionAndSize(Vector2.Zero, new Vector2(Width, Height)),
                    Vector3.UnitZ,
                    frame, 1,
                    mode: VisualizationMode.Surfaces,
                    blendState: BlendState.AlphaBlend,
                    lightDirection: new Vector3(-0.5f, -0.5f, -0.33f)
                );

                var ir = new ImperativeRenderer(
                    frame, Game.Materials, 4
                );
                var layout = Game.Font.LayoutString(
                    string.Format(
                        @"{0:000000} / {1:000000} alive",
                        System.LiveCount, System.Capacity
                    ), position: new Vector2(6, 6)
                );
                ir.DrawMultiple(layout, material: Game.TextMaterial);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.R))
                    Reset();

                for (var i = 0; i < 9; i++) {
                    if (i >= System.Transforms.Count)
                        break;
                    var k = Keys.D1 + i;
                    if (KeyWasPressed(k))
                        System.Transforms[i].IsActive = !System.Transforms[i].IsActive;
                }

                var time = (float)Time.Seconds;

                var sz = new Vector3(Width, Height, 0);

                var grav = System.Transforms.OfType<Gravity>().FirstOrDefault();

                if (grav != null) {
                    grav.Attractors[0].Position = new Vector3(
                        (float)((Math.Sin(time / 6) * 500) + (sz.X / 2)),
                        (float)((Math.Cos(time / 6) * 500) + (sz.Y / 2)),
                        0
                    );
                    grav.Attractors[0].Strength = Arithmetic.PulseExp(time / 4, -240f, -20f) * 60f;

                    grav.Attractors[1].Position = new Vector3(
                        (float)((Math.Sin((time / 2) + 0.7) * 400) + (sz.X * 0.55f)),
                        (float)((Math.Cos((time / 2) + 0.8) * 220) + (sz.Y * 0.43f)),
                        0
                    );
                    grav.Attractors[1].Strength = Arithmetic.PulseExp(time / 3, -90f, 250f) * 60f;

                    grav.Attractors[2].Position = new Vector3(
                        (float)((Math.Sin((time / 13) + 1.2) * 700) + (sz.X / 2)),
                        (float)((Math.Cos((time / 13) + 3.6) * 550) + (sz.Y * 0.55f)),
                        0
                    );
                    grav.Attractors[2].Strength = Arithmetic.PulseExp(time / 6, 2f, 320f) * 60f;

                    grav.Attractors[3].Position = new Vector3(
                        (float)((Math.Sin((time / 16) + 1.2) * 200) + (sz.X / 2)),
                        (float)((Math.Cos((time / 8) + 3.6) * 550) + (sz.Y / 2)),
                        0
                    );
                    grav.Attractors[3].Strength = Arithmetic.PulseExp(time / 8, 4f, 600f) * 60f;
                }

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;
            }

            MaybeSpawnMoreParticles();
        }

        void MaybeSpawnMoreParticles () {
            if (!SpawnFromTemplate)
                return;

            if (FramesUntilNextSpawn > 0) {
                FramesUntilNextSpawn--;
                return;
            }

            FramesUntilNextSpawn = SpawnInterval;

            int seed = 0;

            var width = Pattern.Width;
            var height = Pattern.Height;
            int wrap = (int)(PatternPixels.Length * ParticlesPerPixel);

            var offsetX = (Width - width) / 2f;
            var offsetY = (Height - height) / 2f;
            var totalSpawned = System.Spawn(
                SpawnCount,
                (buf, offset) => {
                    var rng = new MersenneTwister(Interlocked.Increment(ref seed));
                    var scaledWidth = width * ParticlesPerPixel;
                    var invPerPixel = 1.0f / ParticlesPerPixel;
                    for (var i = 0; i < buf.Length; i++) {
                        int j = (i + offset + SpawnOffset) % wrap;
                        var x = (j % scaledWidth) * invPerPixel + offsetX;
                        var y = (j / scaledWidth) + offsetY;

                        buf[i] = new Vector4(
                            x, y, 0,
                            rng.NextFloat(
                                200, 
                                MaxLife
                            ) / 60f
                        );
                    }
                },
                (buf, offset) => {
                    var rng = new MersenneTwister(Interlocked.Increment(ref seed));
                    for (var i = 0; i < buf.Length; i++) {
                        var angle = rng.NextFloat(0, MathHelper.TwoPi);
                        var vel = rng.NextFloat(0, 0.66f) * 60f;
                        buf[i] = new Vector4(
                            (float)Math.Cos(angle) * vel,
                            (float)Math.Sin(angle) * vel,
                            0, 0
                        );
                    }
                },
                (buf, offset) => {
                    float b = 0.66f / ParticlesPerPixel;
                    if (b > 1)
                        b = 1;
                    for (var i = 0; i < buf.Length; i++) {
                        int j = (int)((i + offset + SpawnOffset) % wrap / ParticlesPerPixel);
                        int idx = j % PatternPixels.Length;
                        buf[i] = PatternPixels[idx].ToVector4() * b;
                    };
                }
            );

            SpawnOffset += totalSpawned;
        }

        UTF8String Transforms = new UTF8String("Transforms");

        public unsafe override void UIScene () {
            var ctx = Game.Nuklear.Context;

            if (Nuke.nk_tree_push_hashed(ctx, NuklearDotNet.nk_tree_type.NK_TREE_TAB, Transforms.pText, NuklearDotNet.nk_collapse_states.NK_MAXIMIZED, Transforms.pText, Transforms.Length, 64) != 0) {
                int i = 0;
                foreach (var t in System.Transforms) {
                    using (var temp = new UTF8String(t.GetType().Name)) {
                        var newActive = Nuke.nk_check_text(ctx, temp.pText, temp.Length, t.IsActive ? 0 : 1);
                        t.IsActive = newActive == 0;
                    }
                }

                Nuke.nk_tree_pop(ctx);
            }
        }
    }
}
