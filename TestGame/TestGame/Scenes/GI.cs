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
    public class GlobalIlluminationTest : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        float LightZ;

        const int MultisampleCount = 0;
        const int MaxStepCount = 128;
        const float LightScaleFactor = 4;

        Toggle ShowGBuffer,
            ShowDistanceField,
            TwoPointFiveD;

        Slider DistanceFieldResolution,
            LightmapScaleRatio;

        RendererQualitySettings DirectionalQuality;

        public GlobalIlluminationTest (TestGame game, int width, int height)
            : base(game, width, height) {

            TwoPointFiveD.Value = true;
            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;

            ShowGBuffer.Key = Keys.G;
            TwoPointFiveD.Key = Keys.D2;
            TwoPointFiveD.Changed += (s, e) => Renderer.InvalidateFields();
            ShowDistanceField.Key = Keys.D;

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

            Renderer.CreateGIProbes(32, new Vector2(50, 50));
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 128;
            Environment.ZToYMultiplier = 2.5f;

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width, Height, true, false, enableGlobalIllumination: true
                ) {
                    MaxFieldUpdatesPerFrame = 3,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = true,
                }
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

            DirectionalQuality = new RendererQualitySettings {
                MinStepSize = 2f,
                LongStepFactor = 0.95f,
                MaxConeRadius = 24,
                OcclusionToOpacityPower = 0.7f,
            };

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-0.75f, -0.7f, -0.33f),
                Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f),
                Quality = DirectionalQuality
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.35f, -0.05f, -0.75f),
                Color = new Vector4(0.5f, 0.3f, 0.15f, 0.3f),
                Quality = DirectionalQuality
            });

            Rect(new Vector2(330, 300), new Vector2(Width, 350), 0f, 55f);

            for (int i = 0; i < 8; i++)
                Pillar(new Vector2(30 + (i * 270), 560));
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;
            Renderer.Configuration.RenderScale = Vector2.One * LightmapScaleRatio;
            Renderer.Configuration.RenderSize = new Pair<int>(
                (int)(Renderer.Configuration.MaximumRenderSize.First * LightmapScaleRatio),
                (int)(Renderer.Configuration.MaximumRenderSize.Second * LightmapScaleRatio)
            );

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
                    foreach (var p in Renderer.GIProbes) {
                        lock (p) {
                            if (p.Samples.Count == 0)
                                continue;

                            foreach (var rad in p.Samples) {
                                var c = new Color(rad.Value.X, rad.Value.Y, rad.Value.Z, 1);
                                var center = new Vector2(p.Position.X + (rad.SurfaceNormal.X * -5), p.Position.Y + (rad.SurfaceNormal.Y * -5));
                                var size = new Vector2(2.2f, 2.2f);
                                gb.AddFilledQuad(center - size, center + size, c);
                            }
                        }
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

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                MovableLight.Position = mousePos;
                MovableLight.Color.W = Arithmetic.Pulse((float)Time.Seconds / 3f, 0.3f, 1f);
            }
        }
    }
}
