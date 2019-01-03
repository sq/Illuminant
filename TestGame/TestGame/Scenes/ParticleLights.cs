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
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class ParticleLights : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        float LightZ;

        const int MultisampleCount = 0;
        const int MaxStepCount = 128;
        const float MaxLife = 200;

        [Group("Visualization")]
        Toggle ShowGBuffer, ShowDistanceField;

        [Group("Lighting")]
        Toggle TwoPointFiveD,
            EnableShadows,
            EnableDirectionalLights,
            EnableParticleShadows,
            EnableParticleLights;

        [Group("Resolution")]
        Slider DistanceFieldResolution, LightmapScaleRatio;

        [Group("Particles")]
        Slider StippleFactor, OpacityFromLife;
        [Group("Particles")]
        Toggle ParticleCollisions, ShowParticles;

        RendererQualitySettings DirectionalQuality;

        ParticleEngine Engine;
        ParticleSystem System;

        public ParticleLights (TestGame game, int width, int height)
            : base(game, width, height) {

            TwoPointFiveD.Value = true;
            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;
            EnableShadows.Value = true;
            EnableDirectionalLights.Value = true;
            EnableParticleShadows.Value = true;
            EnableParticleLights.Value = true;
            ParticleCollisions.Value = true;
            ShowParticles.Value = true;
            StippleFactor.Value = 1.0f;

            ShowGBuffer.Key = Keys.G;
            TwoPointFiveD.Key = Keys.D2;
            TwoPointFiveD.Changed += (s, e) => Renderer.InvalidateFields();
            ShowDistanceField.Key = Keys.D;
            EnableShadows.Key = Keys.S;
            EnableDirectionalLights.Key = Keys.D3;
            EnableParticleShadows.Key = Keys.A;
            EnableParticleLights.Key = Keys.P;
            ShowParticles.Key = Keys.O;
            ParticleCollisions.Key = Keys.C;

            DistanceFieldResolution.MinusKey = Keys.D5;
            DistanceFieldResolution.PlusKey = Keys.D6;
            DistanceFieldResolution.Min = 0.1f;
            DistanceFieldResolution.Max = 1.0f;
            DistanceFieldResolution.Speed = 0.05f;

            LightmapScaleRatio.MinusKey = Keys.D7;
            LightmapScaleRatio.PlusKey = Keys.D8;
            LightmapScaleRatio.Min = 0.05f;
            LightmapScaleRatio.Max = 1.0f;
            LightmapScaleRatio.Speed = 0.1f;
            LightmapScaleRatio.Changed += (s, e) => Renderer.InvalidateFields();

            StippleFactor.MinusKey = Keys.Q;
            StippleFactor.PlusKey = Keys.W;
            StippleFactor.Min = 0.1f;
            StippleFactor.Max = 1.0f;
            StippleFactor.Speed = 0.1f;

            OpacityFromLife.MinusKey = Keys.OemSemicolon;
            OpacityFromLife.PlusKey = Keys.OemQuotes;
            OpacityFromLife.Min = 50;
            OpacityFromLife.Max = MaxLife;
            OpacityFromLife.Speed = 10f;
            OpacityFromLife.Value = MaxLife / 2f;

            DistanceFieldResolution.Changed += (s, e) => CreateDistanceField();
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, MultisampleCount, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        HeightVolumeBase Rect (Vector2 a, Vector2 b, float z1, float height) {
            var result = new SimpleHeightVolume(
                Polygon.FromBounds(new Bounds(a, b)), z1, height 
            );
            Environment.HeightVolumes.Add(result);
            return result;
        }

        void Ellipse (Vector2 center, float radiusX, float radiusY, float z1, float height) {
            var numPoints = Math.Max(
                16,
                (int)Math.Ceiling((radiusX + radiusY) * 0.2f)
            );

            var pts = new Vector2[numPoints];
            float radiusStep = (float)((Math.PI * 2) / numPoints);
            float r = 0;

            for (var i = 0; i < numPoints; i++, r += radiusStep)
                pts[i] = new Vector2((float)Math.Cos(r) * radiusX, (float)Math.Sin(r) * radiusY) + center;
            
            var result = new SimpleHeightVolume(
                new Polygon(pts),
                z1, height
            );
            Environment.HeightVolumes.Add(result);
        }

        void Pillar (Vector2 center, float scale) {
            const float totalHeight = 0.69f;
            const float baseHeight  = 0.085f;
            const float capHeight   = 0.09f;

            var baseSizeTL = new Vector2(62, 65) * scale;
            var baseSizeBR = new Vector2(64, 57) * scale;
            Ellipse(center, 51f * scale, 45f * scale, 0, totalHeight * 128);
            Rect(center - baseSizeTL, center + baseSizeBR, 0.0f, baseHeight * 128);
            Rect(center - baseSizeTL, center + baseSizeBR, (totalHeight - capHeight) * 128, capHeight * 128);
        }

        private void CreateDistanceField () {
            if (DistanceField != null) {
                Game.RenderCoordinator.DisposeResource(DistanceField);
                DistanceField = null;
            }

            DistanceField = new DistanceField(
                Game.RenderCoordinator, Width, Height, Environment.MaximumZ,
                32, DistanceFieldResolution.Value
            );

            if (Renderer != null) {
                Renderer.DistanceField = DistanceField;
                Renderer.InvalidateFields();
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 128;
            Environment.ZToYMultiplier = 1.9f;

            for (int i = 0; i < 4; i++)
                Environment.GIVolumes.Add(new GIVolume());

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width, Height, true,
                    enableBrightnessEstimation: false
                ) {
                    MaximumFieldUpdatesPerFrame = 3,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = true
                }, Game.IlluminantMaterials
            );

            CreateDistanceField();

            Engine = new ParticleEngine(
                Game.RenderCoordinator, Game.Materials,
                new ParticleEngineConfiguration (64) {
                }, Game.ParticleMaterials
            );

            SetupParticleSystem();

            DirectionalQuality = new RendererQualitySettings {
                MinStepSize = 2f,
                LongStepFactor = 0.95f,
                MaxConeRadius = 24,
                OcclusionToOpacityPower = 0.7f
            };

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-0.6f, -0.7f, -0.2f),
                Color = new Vector4(0.3f, 0.1f, 0.8f, 0.2f),
                Quality = DirectionalQuality,
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.5f, -0.7f, -0.3f),
                Color = new Vector4(0.1f, 0.8f, 0.3f, 0.2f),
                Quality = DirectionalQuality,
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.12f, 0.7f, -0.7f),
                Color = new Vector4(0.2f, 0.2f, 0.1f, 0.2f),
                Quality = DirectionalQuality,
            });

            Environment.Lights.Add(new ParticleLightSource {
                System = System,
                Template = new SphereLightSource {
                    Radius = 3,
                    RampLength = 40,
                    RampMode = LightSourceRampMode.Linear,
                    Color = Vector4.One * 0.4f
                }
            });

            var floor = -5f;

            Rect(new Vector2(330, 300), new Vector2(Width, 340), floor, 60f);

            for (int i = 0; i < 10; i++) {
                Pillar(new Vector2(10 + (i * 210), 430), 0.63f);
                Pillar(new Vector2(50 + (i * 210), 590), 0.63f);
            }

            Rect(new Vector2(630, 650), new Vector2(Width, 690), floor, 40f);
            Rect(new Vector2(630, 650), new Vector2(670, Height - 40), floor, 40f);
            Rect(new Vector2(630, 790), new Vector2(800, 830), floor, 40f);
            Rect(new Vector2(900, 790), new Vector2(Width, 830), floor, 40f);
            Rect(new Vector2(630, 930), new Vector2(900, 970), floor, 40f);
            Rect(new Vector2(1000, 930), new Vector2(Width - 100, 970), floor, 40f);
        }

        void SetupParticleSystem () {
            var sz = new Vector3(Width, Height, 0);
            var fireball = Game.TextureLoader.Load("fireball");
            var fireballRect = fireball.BoundsFromRectangle(new Rectangle(0, 0, 34, 21));

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration() {
                    Appearance = {
                        Texture = fireball,
                        Region = fireballRect,
                        AnimationRate = new Vector2(1 / 3f, 0),
                        RelativeSize = false
                    },
                    Size = new Vector2(34, 21) * 0.35f,
                    RotationFromVelocity = true,
                    Collision = {
                        EscapeVelocity = 128f,
                        BounceVelocityMultiplier = 1f,
                        LifePenalty = 1,
                    },
                    MaximumVelocity = 128f,
                }
            ) {
                Transforms = {
                    new Spawner {
                        IsActive = false,
                        MinRate = 256,
                        MaxRate = 512,
                        Life = new Formula1 {
                            Offset = 1f,
                            RandomScale = (MaxLife - OpacityFromLife) / 60f
                        },
                        Position = new Formula3 {
                            RandomOffset = new Vector3(-0.5f, -0.5f, -0.5f),
                            RandomScale = new Vector3(15f, 15f, 5f),
                            Type = FormulaType.Spherical
                        },
                        Velocity = new Formula3 {
                            RandomOffset = new Vector3(-0.5f, -0.5f, -0.5f),
                            RandomScale = new Vector3(4f, 4f, 2f) * 60,
                            ConstantRadius = new Vector3(3f, 3f, 0f),
                            Type = FormulaType.Spherical
                        },
                        Attributes = new Formula4 {
                            Constant = new Vector4(0.09f, 0.09f, 0.09f, 0.3f),
                            RandomScale = new Vector4(0.2f, 0.2f, 0.2f, 0.1f)
                        }
                    },
                    new MatrixMultiply {
                        Velocity = Matrix.CreateRotationZ((float)Math.PI * 0.002f),
                    }
                }
            };

            System.OnDeviceReset += (_) => System.Clear();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;
            Renderer.Configuration.SetScale(LightmapScaleRatio);

            // Renderer.InvalidateFields();
            Renderer.UpdateFields(frame, -3);

            var pls = Renderer.Environment.Lights.OfType<ParticleLightSource>().First();
            pls.IsActive = EnableParticleLights;
            pls.Template.CastsShadows = EnableParticleShadows;
            System.Configuration.StippleFactor = StippleFactor.Value;

            System.Configuration.Collision.DistanceField = ParticleCollisions ? DistanceField : null;
            System.Configuration.Collision.DistanceFieldMaximumZ = Environment.MaximumZ;
            System.Configuration.ZToY = TwoPointFiveD ? Environment.ZToYMultiplier : 0;
            System.Update(frame, -2);

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                (dm, _) => {
                    Game.Materials.PushViewTransform(ViewTransform.CreateOrthographic(
                        Width, Height
                    ));
                },
                (dm, _) => {
                    Game.Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(bg, 0, Game.Materials.Clear, clearColor: Color.Black);

                var lighting = Renderer.RenderLighting(
                    bg, 1, 1.0f / 4, true
                );
                lighting.Resolve(
                    bg, 2, Width, Height,
                    hdr: new HDRConfiguration {
                        InverseScaleFactor = 4,
                        Gamma = 1.0f,
                        Dithering = new DitheringSettings {
                            Power = 8,
                            Strength = 1f
                        }
                    }
                );
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                using (var bb = BitmapBatch.New(
                    group, 1,
                    Game.Materials.Get(
                        Game.Materials.ScreenSpaceBitmap, 
                        blendState: BlendState.Opaque
                    ),
                    samplerState: SamplerState.LinearClamp
                ))
                    bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));

                if (ShowDistanceField) {
                    float dfScale = Math.Min(
                        (Game.Graphics.PreferredBackBufferWidth - 4) / (float)Renderer.DistanceField.Texture.Width,
                        (Game.Graphics.PreferredBackBufferHeight - 4) / (float)Renderer.DistanceField.Texture.Height
                    );

                    using (var bb = BitmapBatch.New(
                        group, 3, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.LinearClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField.Texture, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            new Color(255, 255, 255, 255), dfScale
                        ));
                }

                if (ShowGBuffer && Renderer.Configuration.EnableGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 4, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.GBuffer.Texture, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, LightmapScaleRatio
                        ));
                }
            }

            if (ShowParticles)
                System.Render(
                    frame, 10, 
                    blendState: BlendState.Additive
                );

            var ir = new ImperativeRenderer(
                frame, Game.Materials, 11
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
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                LightZ = ((ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ);

                if (LightZ < 12f)
                    LightZ = 12f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                foreach (var d in Environment.Lights.OfType<DirectionalLightSource>()) {
                    d.Opacity = EnableDirectionalLights ? 1 : 0;
                    d.CastsShadows = EnableShadows;
                }

                var s = System.Transforms.OfType<SpawnerBase>().First();
                s.Position.Constant = mousePos;
                System.Configuration.Color.OpacityFromLife = OpacityFromLife.Value / 60f;
                s.IsActive = Game.LeftMouse;
            }
        }
    }
}
