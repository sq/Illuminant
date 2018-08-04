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
        const float MaxLife = 600;

        Toggle ShowGBuffer,
            ShowDistanceField,
            TwoPointFiveD,
            EnableShadows,
            EnableDirectionalLights,
            EnableParticleLights,
            sRGB,
            ParticleCollisions,
            ShowParticles;

        Slider DistanceFieldResolution,
            LightmapScaleRatio,
            LightScaleFactor,
            OpacityFromLife;

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
            EnableParticleLights.Value = true;
            ParticleCollisions.Value = true;
            ShowParticles.Value = true;
            LightScaleFactor.Value = 2.5f;

            ShowGBuffer.Key = Keys.G;
            TwoPointFiveD.Key = Keys.D2;
            TwoPointFiveD.Changed += (s, e) => Renderer.InvalidateFields();
            ShowDistanceField.Key = Keys.D;
            EnableShadows.Key = Keys.S;
            EnableDirectionalLights.Key = Keys.D3;
            EnableParticleLights.Key = Keys.P;
            ShowParticles.Key = Keys.O;
            sRGB.Key = Keys.R;
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

            LightScaleFactor.MinusKey = Keys.Q;
            LightScaleFactor.PlusKey = Keys.W;
            LightScaleFactor.Min = 0.5f;
            LightScaleFactor.Max = 6.0f;
            LightScaleFactor.Speed = 0.5f;

            OpacityFromLife.MinusKey = Keys.OemOpenBrackets;
            OpacityFromLife.PlusKey = Keys.OemCloseBrackets;
            OpacityFromLife.Min = 50;
            OpacityFromLife.Max = 500;
            OpacityFromLife.Speed = 50f;

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
                (int)Math.Ceiling((radiusX + radiusY) * 0.5f)
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
                }
            );

            CreateDistanceField();

            Engine = new ParticleEngine(
                Game.Content, Game.RenderCoordinator, Game.Materials, 
                new ParticleEngineConfiguration ()
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
                Color = new Vector4(0.3f, 0.1f, 0.8f, 0.35f),
                Quality = DirectionalQuality,
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.5f, -0.7f, -0.3f),
                Color = new Vector4(0.1f, 0.8f, 0.3f, 0.35f),
                Quality = DirectionalQuality,
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.12f, 0.7f, -0.7f),
                Color = new Vector4(0.2f, 0.2f, 0.1f, 0.35f),
                Quality = DirectionalQuality,
            });

            Environment.Lights.Add(new ParticleLightSource {
                System = System,
                Template = new SphereLightSource {
                    Color = new Vector4(0.85f, 0.35f, 0.35f, 0.3f),
                    Radius = 4,
                    RampLength = 32,
                    RampMode = LightSourceRampMode.Exponential,
                }
            });

            var floor = 3f;

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
            var fireball = Game.Content.Load<Texture2D>("fireball");
            var fireballRect = fireball.BoundsFromRectangle(new Rectangle(0, 0, 34, 21));

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(
                    attributeCount: 1
                ) {
                    Texture = fireball,
                    TextureRegion = fireballRect,
                    Size = new Vector2(34, 21) * 0.2f,
                    AnimationRate = new Vector2(1 / 6f, 0),
                    RotationFromVelocity = true,
                    EscapeVelocity = 5f,
                    BounceVelocityMultiplier = 0.95f,
                    MaximumVelocity = 16f,
                    CollisionDistance = 1f,
                    CollisionLifePenalty = 4
                }
            ) {
                Transforms = {
                    new Spawner {
                        IsActive = false,
                        MinCount = 32,
                        MaxCount = 1024,
                        Position = new Formula {
                            RandomOffset = new Vector4(-0.5f, -0.5f, 0f, 0f),
                            RandomScale = new Vector4(100f, 100f, 50f, MaxLife - OpacityFromLife),
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
                    new MatrixMultiply {
                        Velocity = Matrix.CreateRotationZ((float)Math.PI * 0.011f) * 1.001f,
                    }
                }
            };

            System.OnDeviceReset += (_) => System.Clear();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;
            Renderer.Configuration.RenderScale = Vector2.One * LightmapScaleRatio;
            Renderer.Configuration.RenderSize = new Pair<int>(
                (int)(Renderer.Configuration.MaximumRenderSize.First * LightmapScaleRatio),
                (int)(Renderer.Configuration.MaximumRenderSize.Second * LightmapScaleRatio)
            );

            // Renderer.InvalidateFields();
            Renderer.UpdateFields(frame, -3);

            var pls = Renderer.Environment.Lights.OfType<ParticleLightSource>().First();
            pls.IsActive = EnableParticleLights;
            pls.Template.CastsShadows = EnableShadows;

            System.Configuration.DistanceField = ParticleCollisions ? DistanceField : null;
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
                    bg, 1, 1.0f / LightScaleFactor, true
                );
                lighting.Resolve(
                    bg, 2, Width, Height,
                    hdr: new HDRConfiguration {
                        InverseScaleFactor = LightScaleFactor,
                        Gamma = sRGB ? 1.8f : 1.0f
                    }, 
                    resolveToSRGB: sRGB
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

                if (ShowParticles)
                    System.Render(
                        group, 2, 
                        material: Engine.ParticleMaterials.AttributeColor,
                        blendState: BlendState.AlphaBlend
                    );

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
                        samplerState: SamplerState.PointClamp
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
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                foreach (var d in Environment.Lights.OfType<DirectionalLightSource>()) {
                    d.Opacity = EnableDirectionalLights ? 1 : 0;
                    d.CastsShadows = EnableShadows;
                }

                var s = System.Transforms.OfType<Spawner>().First();
                s.Position.RandomOffset = new Vector4(mousePos, 0);
                System.Configuration.OpacityFromLife = OpacityFromLife.Value;
                s.IsActive = ms.LeftButton == ButtonState.Pressed;
            }
        }
    }
}
