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
    public class LightProbeTest : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        const int MultisampleCount = 0;
        const int MaxStepCount = 128;
        const float LightScaleFactor = 2;

        Toggle ShowGBuffer,
            ShowLightmap,
            ShowDistanceField,
            UseRampTexture;

        Slider DistanceFieldResolution,
            LightmapScaleRatio;

        public LightProbeTest (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;
            UseRampTexture.Value = true;

            ShowLightmap.Key = Keys.L;
            ShowGBuffer.Key = Keys.G;
            ShowDistanceField.Key = Keys.D;
            UseRampTexture.Key = Keys.P;

            DistanceFieldResolution.MinusKey = Keys.D3;
            DistanceFieldResolution.PlusKey = Keys.D4;
            DistanceFieldResolution.Min = 0.1f;
            DistanceFieldResolution.Max = 1.0f;
            DistanceFieldResolution.Speed = 0.05f;

            LightmapScaleRatio.MinusKey = Keys.D6;
            LightmapScaleRatio.PlusKey = Keys.D7;
            LightmapScaleRatio.Min = 0.05f;
            LightmapScaleRatio.Max = 1.0f;
            LightmapScaleRatio.Speed = 0.1f;

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

        private void CreateDistanceField () {
            if (DistanceField != null) {
                Game.RenderCoordinator.DisposeResource(DistanceField);
                DistanceField = null;
            }

            DistanceField = new DistanceField(
                Game.RenderCoordinator, 1024, 1024, Environment.MaximumZ,
                8, DistanceFieldResolution.Value
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

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024, 1024, true, false
                ) {
                    MaxFieldUpdatesPerFrame = 3,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = false
                }
            );

            CreateDistanceField();

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-0.75f, -0.7f, -0.33f),
                Color = new Vector4(0.25f, 0.45f, 0.65f, 0.6f),
                CastsShadows = false
            });

            Environment.Lights.Add(new SphereLightSource {
                Position = new Vector3(500, 350, 100),
                Color = new Vector4(1f, 0.7f, 0.15f, 1f),
                Radius = 128,
                RampLength = 360
            });

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Box, 
                new Vector3(500, 750, 0), new Vector3(50, 100, 100f)
            ));

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid, 
                new Vector3(300, 250, 0), new Vector3(40, 45, 100f)
            ));

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(50, 50, 1)
            });

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(375, 300, 6)
            });

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(500, 350, 12)
            });

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(500, 450, 18)
            });

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(600, 600, 18)
            });

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(10, 10, 0)
            });

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(10, 700, 0)
            });

            Renderer.Probes.Add(new LightProbe {
                Position = new Vector3(700, 10, 0)
            });

            Renderer.Probes.Add(new LightProbe {
            });
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.Configuration.RenderScale = Vector2.One * LightmapScaleRatio;
            Renderer.Configuration.RenderSize = new Pair<int>(
                (int)(Renderer.Configuration.MaximumRenderSize.First * LightmapScaleRatio),
                (int)(Renderer.Configuration.MaximumRenderSize.Second * LightmapScaleRatio)
            );

            // Renderer.InvalidateFields();
            Renderer.UpdateFields(frame, -2);

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

                var lighting = Renderer.RenderLighting(bg, 1, 1.0f / LightScaleFactor);
                lighting.Resolve(bg, 2, Width, Height, hdr: new HDRConfiguration { InverseScaleFactor = LightScaleFactor });
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                using (var bb = BitmapBatch.New(
                    group, 1,
                    Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                    samplerState: SamplerState.LinearClamp
                ))
                    bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));

                using (var gb = GeometryBatch.New(
                    group, 2,
                    Game.Materials.Get(Game.Materials.ScreenSpaceGeometry, blendState: BlendState.Opaque)
                )) {
                    foreach (var p in Renderer.Probes) {
                        var c = new Color(p.Value.X, p.Value.Y, p.Value.Z, 1);
                        var center = new Vector2(p.Position.X, p.Position.Y);
                        var size = new Vector2(6, 6);
                        gb.AddFilledQuad(center - size, center + size, c);
                        gb.AddOutlinedQuad(center - size, center + size, Color.White);
                    }
                }

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
                const float step = 0.1f;
                
                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                Renderer.Probes.Last().Position = new Vector3(ms.X, ms.Y, 32);

                Environment.Lights.Last().RampTexture = UseRampTexture ? Game.RampTexture : null;
            }
        }
    }
}
