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
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class SimpleParticles : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer LightingRenderer;

        ParticleEngine Engine;
        ParticleSystem System;

        bool Running = true;
        bool ShowDistanceField = false;
        bool Collisions = true;
        int RandomSeed = 201;

        const float ParticlesPerPixel = 4;
        const int   SpawnInterval = 10;
        const int   SpawnCount    = 1024;

        int SpawnOffset = 0;
        int FramesUntilNextSpawn = 0;

        Texture2D Pattern;
        Color[]   PatternPixels;

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
            var spark = Game.Content.Load<Texture2D>("spark");

            Pattern = Game.Content.Load<Texture2D>("template");

            var width = Pattern.Width;
            PatternPixels = new Color[width * Pattern.Height];
            Pattern.GetData(PatternPixels);

            const int opacityFromLife = 400;

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(
                    attributeCount: 1
                ) {
                    Texture = spark,
                    Size = Vector2.One * 2.5f,
                    /*
                    Texture = fireball,
                    TextureRegion = fireballRect,
                    Size = new Vector2(34, 21) * 0.2f,
                    AnimationRate = new Vector2(1 / 6f, 0),
                    */
                    RotationFromVelocity = true,
                    OpacityFromLife = opacityFromLife,
                    EscapeVelocity = 5f,
                    BounceVelocityMultiplier = 0.95f,
                    MaximumVelocity = 16f,
                    CollisionDistance = 1f
                }
            ) {
                Transforms = {
                    new Spawner {
                        MinCount = 2500,
                        MaxCount = 7000,
                        Position = new Formula {
                            Constant = new Vector4(Pattern.Width / 2f, Pattern.Height / 2f, 0, opacityFromLife * 0.5f),
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0f, 0f),
                            RandomScale = new Vector4(900f * 2f, 450f * 2f, 0f, opacityFromLife * 0.5f),
                        },
                        Velocity = new Formula {
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0f, 0f),
                            RandomScale = new Vector4(3f, 3f, 0f, 0f),
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
                    new FMA {
                        Velocity = {
                            Multiply = Vector3.One * 0.9993f
                        }
                    },
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
                }
            };
        }

        void SetupDistanceFieldObjects () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 256;

            DistanceField = new DistanceField(
                Game.RenderCoordinator, Width, Height, Environment.MaximumZ,
                3, 1 / 2f
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
            system.Clear();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();
            
            // This lighting renderer's job is to generate a distance field for collisions
            LightingRenderer.UpdateFields(frame, -3);

            System.Configuration.DistanceField = Collisions ? LightingRenderer.DistanceField : null;

            if (Running)
                System.Update(frame, -2);

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.Black);

            // if (Running)
                System.Render(
                    frame, 1, 
                    material: Engine.ParticleMaterials.AttributeColor,
                    blendState: RenderStates.AdditiveBlend
                );

            var lightDir = new Vector3(-0.5f, 0.5f, -1f);
            lightDir.Normalize();

            if (ShowDistanceField)
                LightingRenderer.VisualizeDistanceField(
                    Bounds.FromPositionAndSize(Vector2.Zero, new Vector2(Width, Height)),
                    Vector3.UnitZ,
                    frame, 2,
                    mode: VisualizationMode.Outlines,
                    blendState: BlendState.AlphaBlend
                );

                var ir = new ImperativeRenderer(
                    frame, Game.Materials, 4,
                    blendState: BlendState.Opaque,
                    samplerState: SamplerState.LinearClamp
                );
                ir.DrawString(
                    Game.Font, string.Format(
                        @"{0} / {1} alive",
                        System.LiveCount, System.Capacity
                    ), new Vector2(6, 6)
                );
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.Space))
                    Running = !Running;
                if (KeyWasPressed(Keys.R))
                    Reset();
                if (KeyWasPressed(Keys.D))
                    ShowDistanceField = !ShowDistanceField;
                if (KeyWasPressed(Keys.C))
                    Collisions = !Collisions;

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
                    grav.Attractors[0].Strength = Arithmetic.PulseExp(time / 4, -240f, -20f);

                    grav.Attractors[1].Position = new Vector3(
                        (float)((Math.Sin((time / 2) + 0.7) * 400) + (sz.X * 0.55f)),
                        (float)((Math.Cos((time / 2) + 0.8) * 220) + (sz.Y * 0.43f)),
                        0
                    );
                    grav.Attractors[1].Strength = Arithmetic.PulseExp(time / 3, -90f, 250f);

                    grav.Attractors[2].Position = new Vector3(
                        (float)((Math.Sin((time / 13) + 1.2) * 700) + (sz.X / 2)),
                        (float)((Math.Cos((time / 13) + 3.6) * 550) + (sz.Y * 0.55f)),
                        0
                    );
                    grav.Attractors[2].Strength = Arithmetic.PulseExp(time / 6, 2f, 320f);

                    grav.Attractors[3].Position = new Vector3(
                        (float)((Math.Sin((time / 16) + 1.2) * 200) + (sz.X / 2)),
                        (float)((Math.Cos((time / 8) + 3.6) * 550) + (sz.Y / 2)),
                        0
                    );
                    grav.Attractors[3].Strength = Arithmetic.PulseExp(time / 8, 4f, 600f);
                }

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
            }

            // MaybeSpawnMoreParticles();
        }

        void MaybeSpawnMoreParticles () {
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
            var totalSpawned = System.Spawn<Vector4>(
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
                                System.Configuration.OpacityFromLife * 0.33f, 
                                System.Configuration.OpacityFromLife
                            )
                        );
                    }
                },
                (buf, offset) => {
                    Array.Clear(buf, 0, buf.Length);
                },
                (buf, offset) => {
                    float b = 0.22f / ParticlesPerPixel;
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
    }
}
