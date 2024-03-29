﻿using System;
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

        Toggle ShowDistanceField,
            UseRampTexture,
            ExponentialRamp,
            EnableProbeShadows;

        Slider RampOffset,
            RampRate,
            DistanceFieldResolution,
            LightmapScaleRatio;

        public LightProbeTest (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;
            UseRampTexture.Value = true;
            EnableProbeShadows.Value = true;

            ShowDistanceField.Key = Keys.D;
            UseRampTexture.Key = Keys.P;
            ExponentialRamp.Key = Keys.E;
            EnableProbeShadows.Key = Keys.S;

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

            RampOffset.Min = (float)-Math.PI;
            RampOffset.Max = (float)Math.PI;
            RampRate.Min = 0.1f;
            RampRate.Max = 10f;
            RampRate.Value = 1f;

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
                16, DistanceFieldResolution.Value
            );
            if (Renderer != null) {
                Renderer.DistanceField = DistanceField;
                Renderer.InvalidateFields();
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 100;

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024, 1024, true, false
                ) {
                    MaximumFieldUpdatesPerFrame = 3,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = false
                }, Game.IlluminantMaterials
            );

            CreateDistanceField();

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-0.75f, -0.7f, -0.33f),
                Color = new Vector4(0.25f, 0.45f, 0.65f, 0.6f),
                ShadowTraceLength = 64
            });

            Environment.Lights.Add(new SphereLightSource {
                Position = new Vector3(500, 350, 1),
                Color = new Vector4(1f, 0.85f, 0.7f, 1f),
                Radius = 128,
                RampLength = 360
            });

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Box, 
                new Vector3(500, 750, 60), new Vector3(50, 100, 100f)
            ));

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid, 
                new Vector3(300, 250, 60), new Vector3(40, 45, 100f)
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

        public override void UnloadContent () {
            DistanceField.Dispose();
            Renderer?.Dispose(); Renderer = null;
        }

        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.Configuration.SetScale(LightmapScaleRatio);

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
                        var center = new Vector2(p.Position.X, p.Position.Y - (p.Position.Z * 0.5f));
                        var size = new Vector2(8, 8);
                        gb.AddFilledQuad(center - size, center + size, c);
                        gb.AddOutlinedQuad(center - size, center + size, Color.Silver);
                        var n = p.Normal.GetValueOrDefault(Vector3.Zero);
                        float sz2 = 20;
                        var ep = center + new Vector2(n.X * sz2, n.Y * sz2);
                        gb.AddLine(center, ep, Color.White);
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
                        samplerState: SamplerState.LinearClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField.Texture.Get(), Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            new Color(255, 255, 255, 255), dfScale
                        ));
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;
                
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                var mousePos = new Vector3(ms.X, ms.Y, 1);
                if (Game.LeftMouse) {
                    var d = mousePos - Renderer.Probes.Last().Position;
                    d.Z = 0;
                    if (d.Length() >= 1) {
                        d.Normalize();
                        Renderer.Probes.Last().Normal = d;
                    } else {
                        Renderer.Probes.Last().Normal = null;
                    }
                } else {
                    Renderer.Probes.Last().Position = mousePos;
                }

                var l = (SphereLightSource)Environment.Lights.Last();
                l.RampTexture.Set(UseRampTexture ? Game.RampTexture : null);
                l.RampMode = ExponentialRamp ? LightSourceRampMode.Exponential : LightSourceRampMode.Linear;
                l.RampOffset = RampOffset;
                l.RampRate = RampRate;

                foreach (var p in Renderer.Probes)
                    p.EnableShadows = EnableProbeShadows;
            }
        }
    }
}
