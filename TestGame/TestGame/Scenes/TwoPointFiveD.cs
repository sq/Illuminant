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
    public class TwoPointFiveDTest : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        Texture2D Background;
        float LightZ;

        const int MultisampleCount = 0;
        const int LightmapScaleRatio = 1;
        const int MaxStepCount = 128;

        bool ShowGBuffer       = false;
        bool ShowLightmap      = false;
        bool ShowDistanceField = false;
        bool ShowHistogram     = true;
        bool UseRampTexture    = true;
        bool Timelapse         = false;
        bool TwoPointFiveD     = true;
        bool Deterministic     = true;

        object HistogramLock = new object();
        Histogram Histogram, NextHistogram;

        public TwoPointFiveDTest (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            Histogram = new Histogram(2f, 2);
            NextHistogram = new Histogram(2f, 2);
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

        void Pillar (Vector2 center) {
            const float totalHeight = 0.69f;
            const float baseHeight  = 0.085f;
            const float capHeight   = 0.09f;

            var baseSizeTL = new Vector2(62, 65);
            var baseSizeBR = new Vector2(64, 57);
            Ellipse(center, 51f, 45f, 0, totalHeight * 128);
            Rect(center - baseSizeTL, center + baseSizeBR, 0.0f, baseHeight * 128);
            Rect(center - baseSizeTL, center + baseSizeBR, (totalHeight - capHeight) * 128, capHeight * 128);
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 128;
            Environment.ZToYMultiplier = 2.5f;

            DistanceField = new DistanceField(
                Game.RenderCoordinator, 1024, 1024, Environment.MaximumZ,
                64, 0.33f
            );

            Background = Game.Content.Load<Texture2D>("sc3test");

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024 / LightmapScaleRatio, 1024 / LightmapScaleRatio, true, true
                ) {
                    RenderScale = Vector2.One * (1.0f / LightmapScaleRatio),
                    DistanceFieldMinStepSize = 1f,
                    DistanceFieldLongStepFactor = 0.5f,
                    DistanceFieldOcclusionToOpacityPower = 0.7f,
                    DistanceFieldMaxConeRadius = 24,
                    DistanceFieldUpdateRate = 6,
                }
            ) {
                DistanceField = DistanceField
            };

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
                Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f)
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.35f, -0.05f, -0.75f),
                Color = new Vector4(0.5f, 0.3f, 0.15f, 0.3f)
            });

            Rect(new Vector2(330, 337), new Vector2(Width, 394), 0f, 55f);

            Pillar(new Vector2(97, 523));
            Pillar(new Vector2(719, 520));

            if (true)
                Environment.Obstructions.Add(new LightObstruction(
                    LightObstructionType.Box, 
                    new Vector3(500, 750, 0), new Vector3(50, 100, 15f)
                ));

            if (false)
                Environment.Obstructions.Add(new LightObstruction(
                    LightObstructionType.Ellipsoid, 
                    new Vector3(500, 750, 0), new Vector3(90, 45, 30f)
                ));

            if (false)
                Environment.HeightVolumes.Clear();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;

            Renderer.InvalidateFields();
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

                var scaleFactor = 0.5f;

                Renderer.RenderLighting(bg, 1, scaleFactor);
                Renderer.ResolveLighting(bg, 2, Width, Height, hdr: new HDRConfiguration { InverseScaleFactor = 1.0f / scaleFactor });
                Renderer.EstimateBrightness(
                    NextHistogram, 
                    (h) => {
                        lock (HistogramLock) {
                            if (h != NextHistogram)
                                return;

                            NextHistogram = Histogram;
                            Histogram = h;
                        }
                    }, 1.0f / scaleFactor
                );
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
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    )) {
                        var dc = new BitmapDrawCall(
                            Background, Vector2.Zero, Color.White * (ShowGBuffer ? 0.7f : 1.0f)
                        );
                        dc.Textures = new TextureSet(dc.Textures.Texture1, Lightmap);
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
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField.Texture, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
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
                            Renderer.GBuffer.Texture, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, LightmapScaleRatio
                        ));
                }

                if (ShowHistogram)
                    DrawHistogram(group, 5);
            }
        }

        private void DrawHistogram (IBatchContainer group, int layer) {
            var ir = new ImperativeRenderer(group, Game.Materials, layer).MakeSubgroup();
            ir.AutoIncrementLayer = true;

            var width = 600;
            var height = 800;
            var bounds = Bounds.FromPositionAndSize(new Vector2(Width + 10, 10), new Vector2(width, height));
            ir.FillRectangle(bounds, Color.Black);
            ir.OutlineRectangle(bounds, Color.White);

            int i = 0;
            float x1 = bounds.TopLeft.X;
            float bucketWidth = width / Histogram.BucketCount;

            ir.AutoIncrementLayer = false;

            Histogram h;

            lock (HistogramLock)
                h = Histogram;

            lock (h) {
                float maxCount = h.SampleCount;
                foreach (var bucket in h.Buckets) {
                    var scaledCount = bucket.Count / maxCount;
                    var y2 = bounds.BottomRight.Y;
                    var y1 = y2 - (scaledCount * height);
                    var x2 = x1 + bucketWidth;

                    ir.FillRectangle(
                        new Bounds(new Vector2(x1, y1), new Vector2(x2, y2)), 
                        Color.Silver
                    ); 

                    x1 = x2;
                    i++;
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

                if (KeyWasPressed(Keys.D2)) {
                    TwoPointFiveD = !TwoPointFiveD;
                    Renderer.InvalidateFields();
                }

                if (KeyWasPressed(Keys.T))
                    Timelapse = !Timelapse;

                if (KeyWasPressed(Keys.D))
                    ShowDistanceField = !ShowDistanceField;

                if (KeyWasPressed(Keys.H))
                    ShowHistogram = !ShowHistogram;

                if (KeyWasPressed(Keys.P))
                    UseRampTexture = !UseRampTexture;

                if (KeyWasPressed(Keys.R))
                    Deterministic = !Deterministic;

                var time = (float)Time.Seconds;

                Renderer.Configuration.DistanceFieldMaxStepCount =
                    (Timelapse & !Deterministic)
                        ? (int)Arithmetic.Clamp((time % 12) * (MaxStepCount / 32.0f), 1, MaxStepCount)
                        : MaxStepCount;

                if (!Deterministic) {
                    var obs = Environment.Obstructions[0];
                    obs.Center =
                        new Vector3(500, 750, Arithmetic.Pulse(time / 10, 0, 40));

                    Renderer.InvalidateFields();
                }

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                MovableLight.RampTexture = UseRampTexture ? Game.RampTexture : null;

                if (Deterministic)
                    MovableLight.Position = new Vector3(671, 394, 97.5f);
                else {
                    MovableLight.Position = mousePos;
                    MovableLight.Color.W = Arithmetic.Pulse((float)Time.Seconds / 2.25f, 0.3f, 1.25f);
                }
            }
        }
    }
}
