
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
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class SC3 : Scene {
        LightingEnvironment Environment, ForegroundEnvironment;
        LightingRenderer Renderer, ForegroundRenderer;

        RenderTarget2D Lightmap, ForegroundLightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        Texture2D Background, Foreground;
        Texture2D Tree;
        Texture2D[] Pillars;
        float LightZ;

        const int LightmapScaleRatio = 1;

        bool ShowGBuffer       = false;
        bool ShowLightmap      = false;
        bool ShowDistanceField = false;
        bool Deterministic     = true;

        public SC3 (TestGame game, int width, int height)
            : base(game, 1396, 768) {
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, 
                    RenderTargetUsage.PlatformContents
                );
            }

            if (ForegroundLightmap == null) {
                if (ForegroundLightmap != null)
                    ForegroundLightmap.Dispose();

                ForegroundLightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();
            ForegroundEnvironment = new LightingEnvironment();

            Background = Game.Content.Load<Texture2D>("bg_noshadows");
            Foreground = Game.Content.Load<Texture2D>("fg");
            Tree = Game.Content.Load<Texture2D>("tree");
            Pillars = new[] {
                Game.Content.Load<Texture2D>("pillar1"),
                Game.Content.Load<Texture2D>("pillar2"),
                Game.Content.Load<Texture2D>("pillar3"),
            };

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width / LightmapScaleRatio, Height / LightmapScaleRatio,
                    Width, Height, 16
                ) {
                    RenderScale = 1.0f / LightmapScaleRatio,
                    DistanceFieldResolution = 0.5f,
                    DistanceFieldMinStepSize = 1f,
                    DistanceFieldLongStepFactor = 0.5f,
                    DistanceFieldOcclusionToOpacityPower = 0.8f,
                    DistanceFieldMaxConeRadius = 16,
                    TwoPointFiveD = true,
                    DistanceFieldUpdateRate = 2
                }
            );

            ForegroundRenderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width / LightmapScaleRatio, Height / LightmapScaleRatio,
                    Width, Height, 2
                ) {
                    RenderScale = 1.0f / LightmapScaleRatio,
                    DistanceFieldResolution = 0.5f,
                    DistanceFieldMinStepSize = 1f,
                    DistanceFieldLongStepFactor = 0.5f,
                    DistanceFieldOcclusionToOpacityPower = 0.8f,
                    DistanceFieldMaxConeRadius = 16,
                    TwoPointFiveD = true,
                    DistanceFieldUpdateRate = 2
                }
            );

            var light = new LightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Radius = 400,
                RampLength = 200,
                RampMode = LightSourceRampMode.Linear,
                AmbientOcclusionRadius = 16f
            };

            Environment.Lights.Add(light);

            light = light.Clone();
            light.AmbientOcclusionRadius = 0;
            ForegroundEnvironment.Lights.Add(light);

            var ambientLight = new LightSource {
                Position = new Vector3(Width / 2f, Height / 2f, 0f),
                Color = new Vector4(0f, 0.8f, 0.6f, 0.2f),
                Radius = 8192,
                RampLength = 0,
                RampMode = LightSourceRampMode.None,
                CastsShadows = false,
            };

            Environment.Lights.Add(ambientLight);

            ambientLight = ambientLight.Clone();
            ambientLight.CastsShadows = false;
            ForegroundEnvironment.Lights.Add(ambientLight);

            BuildObstacles();

            Environment.GroundZ = ForegroundEnvironment.GroundZ = 0;
            Environment.MaximumZ = ForegroundEnvironment.MaximumZ = 128;
            Environment.ZToYMultiplier = ForegroundEnvironment.ZToYMultiplier = 2.5f;
        }

        private void Pillar (float x, float y, int textureIndex) {
            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid,
                new Vector3(x, y, 0),
                new Vector3(18, 8, 100) 
            ));

            var tex = Pillars[textureIndex];
            ForegroundEnvironment.Billboards.Add(new Billboard {
                Position = new Vector3(x - (tex.Width * 0.5f), y - tex.Height + 11, 0),
                Size = new Vector3(tex.Width, tex.Height, 0),
                Texture = tex
            });
        }

        private void BuildObstacles () {
            Pillar(722, 186, 2);
            Pillar(849, 209, 0);
            Pillar(927, 335, 0);
            Pillar(888, 480, 1);
            Pillar(723, 526, 2);
            Pillar(593, 505, 2);
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.UpdateFields(frame, -2);
            ForegroundRenderer.UpdateFields(frame, -2);

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

                Renderer.RenderLighting(bg, 1);

                Renderer.ResolveLighting(bg, 2, new BitmapDrawCall(Renderer.Lightmap, Vector2.Zero, LightmapScaleRatio));
            };

            using (var fg = BatchGroup.ForRenderTarget(
                frame, -1, ForegroundLightmap,
                (dm, _) => {
                    Game.Materials.PushViewTransform(ViewTransform.CreateOrthographic(
                        Width, Height
                    ));
                },
                (dm, _) => {
                    Game.Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(fg, 0, Game.Materials.Clear, clearColor: Color.Black);

                ForegroundRenderer.RenderLighting(fg, 1);

                ForegroundRenderer.ResolveLighting(fg, 2, new BitmapDrawCall(ForegroundRenderer.Lightmap, Vector2.Zero, LightmapScaleRatio));
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                if (ShowLightmap) {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                        samplerState: SamplerState.LinearClamp
                    ))
                        bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));
                } else {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(
                            ShowGBuffer
                                ? Game.Materials.ScreenSpaceBitmap
                                : Game.Materials.ScreenSpaceLightmappedBitmap,
                            blendState: 
                                ShowGBuffer
                                ? BlendState.Opaque
                                : BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    )) {
                        var dc = new BitmapDrawCall(
                            Background, Vector2.Zero, Color.White * (ShowGBuffer ? 0.7f : 1.0f)
                        );
                        dc.Textures = new TextureSet(dc.Textures.Texture1, Lightmap);
                        bb.Add(dc);

                        if (!ShowGBuffer) {
                            dc = new BitmapDrawCall(
                                Foreground, Vector2.Zero
                            );
                            dc.Textures = new TextureSet(dc.Textures.Texture1, ForegroundLightmap);
                            dc.SortOrder = 1;
                            bb.Add(dc);
                        }
                    }
                }

                if (ShowDistanceField) {
                    float dfScale = Math.Min(
                        (Game.Graphics.PreferredBackBufferWidth - 4) / (float)Renderer.DistanceField.Width,
                        (Game.Graphics.PreferredBackBufferHeight - 4) / (float)Renderer.DistanceField.Height
                    );

                    using (var bb = BitmapBatch.New(
                        group, 3, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            new Color(255, 255, 255, 255), dfScale
                        ));
                }

                if (ShowGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 4, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            ForegroundRenderer.GBuffer, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, LightmapScaleRatio
                        ));
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.L))
                    ShowLightmap = !ShowLightmap;

                if (KeyWasPressed(Keys.G))
                    ShowGBuffer = !ShowGBuffer;

                if (KeyWasPressed(Keys.D))
                    ShowDistanceField = !ShowDistanceField;

                if (KeyWasPressed(Keys.R))
                    Deterministic = !Deterministic;

                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 1024.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                if (Deterministic)
                    Environment.Lights[0].Position = ForegroundEnvironment.Lights[0].Position = 
                        new Vector3(Width / 2f, Height / 2f, 200f);
                else
                    Environment.Lights[0].Position = ForegroundEnvironment.Lights[0].Position = 
                        mousePos;
            }
        }

        public override string Status {
            get {
                return string.Format(
                    "L@{1:0000},{2:0000},{0:000.0}", 
                    LightZ, Environment.Lights[0].Position.X, Environment.Lights[0].Position.Y
                );
            }
        }
    }
}
