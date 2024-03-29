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
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class DynamicObstructions : Scene {
        DynamicDistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        float LightZ;

        const float LightScaleFactor = 4;

        Toggle ShowGBuffer,
            ShowDistanceField,
            Deterministic,
            EfficientUpdates,
            Orthographic;

        Slider DistanceFieldResolution;

        List<LightObstruction> Obstructions = new List<LightObstruction>();
        Vector3[] Centers;

        public DynamicObstructions (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            Deterministic.Value = false;
            DistanceFieldResolution.Value = 0.5f;
            EfficientUpdates.Value = true;

            ShowGBuffer.Key = Keys.G;
            ShowDistanceField.Key = Keys.D;
            Deterministic.Key = Keys.R;
            EfficientUpdates.Key = Keys.E;

            Orthographic.Key = Keys.O;
            Orthographic.Value = true;

            DistanceFieldResolution.MinusKey = Keys.D5;
            DistanceFieldResolution.PlusKey = Keys.D6;
            DistanceFieldResolution.Min = 0.1f;
            DistanceFieldResolution.Max = 1.0f;
            DistanceFieldResolution.Speed = 0.05f;

            DistanceFieldResolution.Changed += (s, e) => CreateDistanceField();
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, 1, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        HeightVolumeBase Rect (Vector2 a, Vector2 b, float z1, float height) {
            var result = new SimpleHeightVolume(
                Polygon.FromBounds(new Bounds(a, b)), z1, height 
            ) { IsDynamic = false };
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
            ) { IsDynamic = false };
            Environment.HeightVolumes.Add(result);
        }

        void Pillar (Vector2 center) {
            const float totalHeight = 0.5f;

            var scale = 0.25f;
            var baseSizeTL = new Vector2(62, 65) * scale;
            var baseSizeBR = new Vector2(64, 57) * scale;
            Ellipse(center, 51f * scale, 45f * scale, 0, totalHeight * Environment.MaximumZ);
        }

        private void CreateDistanceField () {
            if (DistanceField != null) {
                Game.RenderCoordinator.DisposeResource(DistanceField);
                DistanceField = null;
            }

            DistanceField = new DynamicDistanceField(
                Game.RenderCoordinator, 1024, 1024, Environment.MaximumZ,
                64, DistanceFieldResolution.Value
            );
            if (Renderer != null) {
                Renderer.DistanceField = DistanceField;
                Renderer.InvalidateFields();
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();
            Obstructions.Clear();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 128;
            Environment.ZToYMultiplier = 1.5f;

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024, 1024, true
                ) {
                    MaximumFieldUpdatesPerFrame = 9,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = true,
                    TwoPointFiveD = true
                }, Game.IlluminantMaterials
            );

            CreateDistanceField();

            MovableLight = new SphereLightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Radius = 24,
                RampLength = 550,
                RampMode = LightSourceRampMode.Linear
            };

            Environment.Lights.Add(MovableLight);

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-0.75f, -0.7f, -0.33f),
                Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f),
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.35f, -0.05f, -0.75f),
                Color = new Vector4(0.5f, 0.3f, 0.15f, 0.3f),
            });

            Rect(new Vector2(330, 347), new Vector2(Width, 388), 0f, 55f);

            for (int x = -1024; x < (Width + 1024); x += 37)
                Pillar(new Vector2(x, 540 + (x / 24.0f)));

            foreach (var o in Environment.Obstructions)
                o.IsDynamic = false;

            var count = 1024;
            var size = 16;
            Centers = new Vector3[count];
            var rng = new MersenneTwister();
            for (int i = 0; i < count; i++) {
                Centers[i] = new Vector3(rng.NextFloat(0, Width), rng.NextFloat(0, Height), 0);
                var obs = new LightObstruction(
                    LightObstructionType.Ellipsoid, Centers[i], new Vector3(size)
                ) {
                    IsDynamic = true
                };
                Obstructions.Add(obs);
                Environment.Obstructions.Add(obs);
            }
        }

        public override void UnloadContent () {
            DistanceField.Dispose();
            Renderer?.Dispose(); Renderer = null;
        }

        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            var scaleRatio = 1.0f;

            Renderer.Configuration.TwoPointFiveD = Orthographic;
            Renderer.Configuration.PerspectiveProjection = !Orthographic;
            Renderer.Configuration.SetScale(scaleRatio);

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
                lighting.Resolve(
                    bg, 2, Width, Height, 
                    hdr: new HDRConfiguration {
                        InverseScaleFactor = LightScaleFactor,
                    }
                );
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                if (ShowGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.AlphaBlend),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(Renderer.GBuffer.Texture.Get(), Vector2.Zero));
                } else {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.ScreenSpaceBitmap,
                        samplerState: SamplerState.PointClamp
                    )) {
                        var dc = new BitmapDrawCall(
                            Lightmap, Vector2.Zero, Color.White
                        );
                        bb.Add(dc);
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

                if (!EfficientUpdates)
                    Renderer.DistanceField.Invalidate();

                if (!Deterministic) {
                    for (int i = 0; i < Centers.Length; i++) {
                        var angle = time * 0.33f;
                        float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                        var obs = Obstructions[i];
                        obs.Center = Centers[i] + new Vector3(x * 24, y * 24, 0);
                    }
                }

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                if (Deterministic) {
                    MovableLight.Position = new Vector3(671, 420, 92.5f);
                } else {
                    MovableLight.Position = mousePos;
                }
            }
        }
    }
}
