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
    public class TwoPointFiveDTest : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        Texture2D Background;
        float LightZ;

        const int MultisampleCount = 0;
        const int MaxStepCount = 128;
        const float LightScaleFactor = 4;

        [Group("Visualization")]
        Toggle ShowGBuffer, 
            ShowDistanceField, 
            ShowLightmap, 
            ShowHistogram;

        [Group("Lighting")]
        Toggle TwoPointFiveD,
            UseRampTexture,
            UseDistanceRamp;
        [Group("Lighting")]
        Slider MaximumLightStrength;
        [Group("Dithering")]
        [Items("None")]
        [Items("Pre")]
        [Items("Post")]
        [Items("Pre+Post")]
        [Items("Merged")]
        Dropdown<string> DitherMode;
        [Group("Dithering")]
        Slider
            DitherStrength,
            DitherPower,
            DitherBandSize,
            DitherRangeMin,
            DitherRangeMax;

        [Group("Resolution")]
        Slider DistanceFieldResolution,
            LightmapScaleRatio,
            MaximumEncodedDistance;

        Toggle Timelapse,
            Deterministic,
            sRGB;

        RendererQualitySettings DirectionalQuality;

        Histogram Histogram;

        public TwoPointFiveDTest (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            Histogram = new Histogram(4f, 2f);

            Deterministic.Value = true;
            ShowHistogram.Value = true;
            UseRampTexture.Value = false;
            TwoPointFiveD.Value = true;
            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;
            MaximumLightStrength.Value = 4f;
            MaximumEncodedDistance.Value = 128;
            DitherStrength.Value = 1f;
            DitherPower.Value = 8;
            DitherBandSize.Value = 1f;
            DitherRangeMin.Value = 0f;
            DitherRangeMax.Value = 1f;
            DitherMode.Value = "Pre+Post";

            ShowLightmap.Key = Keys.L;
            ShowGBuffer.Key = Keys.G;
            TwoPointFiveD.Key = Keys.D2;
            TwoPointFiveD.Changed += (s, e) => Renderer.InvalidateFields();
            Timelapse.Key = Keys.T;
            ShowDistanceField.Key = Keys.D;
            ShowHistogram.Key = Keys.H;
            UseRampTexture.Key = Keys.P;
            Deterministic.Key = Keys.R;
            sRGB.Key = Keys.S;

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

            MaximumLightStrength.Min = 1.0f;
            MaximumLightStrength.Max = 6.0f;
            MaximumLightStrength.Speed = 0.1f;

            MaximumEncodedDistance.Min = 32;
            MaximumEncodedDistance.Max = 512;
            MaximumEncodedDistance.Speed = 16;
            MaximumEncodedDistance.Changed += (s, e) => CreateDistanceField();

            InitUnitSlider(DitherStrength, DitherBandSize, DitherRangeMax, DitherRangeMin);

            DitherPower.Max = 12;
            DitherPower.Min = 1;
            DitherPower.Speed = 1;

            DitherRangeMin.Max =
                DitherRangeMax.Max = 2.0f;

            DistanceFieldResolution.Changed += (s, e) => CreateDistanceField();
        }

        private void InitUnitSlider (params Slider[] sliders) {
            foreach (var s in sliders) {
                s.Max = 1.0f;
                s.Min = 0.0f;
                s.Speed = 0.05f;
            }
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
                8,
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
                Game.RenderCoordinator, 1024, 1024, Environment.MaximumZ,
                64, DistanceFieldResolution.Value, (int)MaximumEncodedDistance.Value
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
            Environment.ZToYMultiplier = 2.5f;
            
            Background = Game.Content.Load<Texture2D>("sc3test");

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024, 1024, true, true
                ) {
                    MaximumFieldUpdatesPerFrame = 3,
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

            var dmode = DitherMode.Value ?? "None";

            var m = Game.Materials;
            m.DefaultDitheringSettings.Power = (int)DitherPower.Value;
            m.DefaultDitheringSettings.Strength = dmode.Contains("Post") ? DitherStrength : 0f;
            m.DefaultDitheringSettings.BandSize = DitherBandSize;
            m.DefaultDitheringSettings.RangeMin = DitherRangeMin;
            m.DefaultDitheringSettings.RangeMax = DitherRangeMax;

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
                lighting.Resolve(
                    bg, 2, Width, Height,
                    hdr: new HDRConfiguration {
                        InverseScaleFactor = LightScaleFactor,
                        Gamma = sRGB ? 2.3f : 1.0f,
                        ResolveToSRGB = sRGB,
                        Dithering = new DitheringSettings {
                            Strength = dmode.Contains("Pre") ? DitherStrength : 0f,
                            Power = (int)DitherPower,
                            BandSize = DitherBandSize,
                            RangeMin = DitherRangeMin,
                            RangeMax = DitherRangeMax
                        }
                    }
                );

                lighting.TryComputeHistogram(
                    Histogram, 
                    null
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
                                : (sRGB 
                                    ? Game.Materials.ScreenSpaceLightmappedsRGBBitmap
                                    : Game.Materials.ScreenSpaceLightmappedBitmap),
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

                if (ShowHistogram) {
                    var visualizer = new HistogramVisualizer {
                        Materials = Game.Materials,
                        Bounds = Bounds.FromPositionAndSize(new Vector2(Width + 10, 10), new Vector2(600, 800))
                    };
                    visualizer.Draw(group, 5, Histogram, new[] { 40.0f, 90.0f });
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;
                
                var time = (float)Time.Seconds;

                Renderer.Configuration.DefaultQuality.MaxStepCount =
                    (Timelapse & !Deterministic)
                        ? (int)Arithmetic.Wrap(time * (MaxStepCount / 8.0f), 1, MaxStepCount)
                        : MaxStepCount;

                DirectionalQuality.MaxStepCount =
                    (int)(Renderer.Configuration.DefaultQuality.MaxStepCount * 0.75f) + 1;

                if (!Deterministic) {
                    var obs = Environment.Obstructions[0];
                    obs.Center =
                        new Vector3(500, 750, Arithmetic.Pulse(time / 10, 0, 40));

                    Renderer.InvalidateFields();
                }

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                MovableLight.RampTexture = UseRampTexture ? Game.RampTexture : null;
                MovableLight.UseDistanceForRampTexture = UseDistanceRamp;

                if (Deterministic) {
                    MovableLight.Position = new Vector3(671, 394, 97.5f);
                    MovableLight.Color.W = 0.5f;
                } else {
                    MovableLight.Position = mousePos;
                    MovableLight.Color.W = Arithmetic.Pulse((float)Time.Seconds / 3f, 0.3f, MaximumLightStrength);
                }
            }
        }
    }
}
