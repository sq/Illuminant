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
        bool Collisions = false;
        int RandomSeed = 201;

        Texture2D Pattern;

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

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(
                    attributeCount: 1
                ) {
                    Texture = spark,
                    Size = Vector2.One * 5.2f,
                    /*
                    Texture = fireball,
                    TextureRegion = fireballRect,
                    Size = new Vector2(34, 21) * 0.2f,
                    AnimationRate = new Vector2(1 / 6f, 0),
                    */
                    RotationFromVelocity = true,
                    OpacityFromLife = 4096,
                    EscapeVelocity = 3f,
                    BounceVelocityMultiplier = 0.95f,
                    MaximumVelocity = 4f,
                    CollisionDistance = 1f
                }
            ) {
                Transforms = {
                    new Gravity {
                        IsActive = false,
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
                        IsActive = false,
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
            int seed = 0;

            var width = Pattern.Width;
            var template = new Color[width * Pattern.Height];
            Pattern.GetData(template);

            var offsetX = (Width - width) / 2f;
            var offsetY = (Height - Pattern.Height) / 2f;

            system.Initialize<Vector4>(
                true ? 10240 : template.Length * 2,
                (buf, offset) => {
                    var rng = new MersenneTwister(Interlocked.Increment(ref seed));
                    for (var i = 0; i < buf.Length; i++) {
                        int j = (i + offset) / 2;
                        var x = (((i + offset) / 2.0f) % width) + offsetX;
                        var y = (j / width) + offsetY;

                        buf[i] = new Vector4(
                            x, y, 0,
                            rng.NextFloat(
                                system.Configuration.OpacityFromLife * 0.9f, 
                                system.Configuration.OpacityFromLife
                            )
                        );
                    }
                },
                (buf, offset) => {
                    Array.Clear(buf, 0, buf.Length);
                },
                (buf, offset) => {
                    for (var i = 0; i < buf.Length; i++) {
                        int j = (i + offset) / 2;
                        if (j < template.Length)
                            buf[i] = template[j].ToVector4() * 0.11f;
                        else
                            buf[i] = Vector4.Zero;
                    };
                }
            );
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

                if (System.Transforms.Count >= 1) {
                    var grav = (Gravity)System.Transforms[0];

                    grav.Attractors[0].Position = new Vector3(
                        (float)((Math.Sin(time / 6) * 500) + (sz.X / 2)),
                        (float)((Math.Cos(time / 6) * 500) + (sz.Y / 2)),
                        0
                    );
                    grav.Attractors[0].Strength = Arithmetic.PulseExp(time / 4, -80f, -20f);

                    grav.Attractors[1].Position = new Vector3(
                        (float)((Math.Sin((time / 2) + 0.7) * 400) + (sz.X * 0.55f)),
                        (float)((Math.Cos((time / 2) + 0.8) * 220) + (sz.Y * 0.43f)),
                        0
                    );
                    grav.Attractors[1].Strength = Arithmetic.PulseExp(time / 3, -50f, 200f);

                    grav.Attractors[2].Position = new Vector3(
                        (float)((Math.Sin((time / 13) + 1.2) * 700) + (sz.X / 2)),
                        (float)((Math.Cos((time / 13) + 3.6) * 550) + (sz.Y * 0.55f)),
                        0
                    );
                    grav.Attractors[2].Strength = Arithmetic.PulseExp(time / 6, 2f, 220f);

                    grav.Attractors[3].Position = new Vector3(
                        (float)((Math.Sin((time / 16) + 1.2) * 200) + (sz.X / 2)),
                        (float)((Math.Cos((time / 8) + 3.6) * 550) + (sz.Y / 2)),
                        0
                    );
                    grav.Attractors[3].Strength = Arithmetic.PulseExp(time / 8, 4f, 460f);
                }

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
            }
        }
    }
}
