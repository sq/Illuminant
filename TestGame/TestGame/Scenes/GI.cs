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

        const int BounceCount = 3;
        const int MultisampleCount = 0;
        const int MaxStepCount = 128;
        const float AORadius = 12;
        const float AOOpacity = 0.5f;
        const float ProbeZ = 1;
        const float ProbeVisBrightness = 1.1f;

        Toggle ShowGBuffer,
            ShowDistanceField,
            TwoPointFiveD,
            RenderDirectLight,
            EnableShadows,
            ShowProbeSH,
            EnablePointLight,
            EnableDirectionalLights,
            AdditiveIndirectLight,
            EdgeShadows;

        Slider DistanceFieldResolution,
            LightmapScaleRatio,
            IndirectLightBrightness,
            BounceDistance,
            ProbeInterval,
            GISourceBounce,
            LightScaleFactor;

        RendererQualitySettings DirectionalQuality;

        public GlobalIlluminationTest (TestGame game, int width, int height)
            : base(game, width, height) {

            TwoPointFiveD.Value = true;
            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;
            RenderDirectLight.Value = true;
            ShowProbeSH.Value = false;
            EnableShadows.Value = true;
            EnablePointLight.Value = true;
            EnableDirectionalLights.Value = false;
            IndirectLightBrightness.Value = 1.0f;
            AdditiveIndirectLight.Value = false;
            BounceDistance.Value = 1024;
            ProbeInterval.Value = 48;
            GISourceBounce.Value = 2;
            LightScaleFactor.Value = 4.0f;
            EdgeShadows.Value = true;

            ShowGBuffer.Key = Keys.G;
            TwoPointFiveD.Key = Keys.D2;
            TwoPointFiveD.Changed += (s, e) => Renderer.InvalidateFields();
            ShowDistanceField.Key = Keys.D;
            RenderDirectLight.Key = Keys.L;
            ShowProbeSH.Key = Keys.P;
            EnableShadows.Key = Keys.S;
            EnableDirectionalLights.Key = Keys.D3;
            EnablePointLight.Key = Keys.D4;
            AdditiveIndirectLight.Key = Keys.A;
            EdgeShadows.Key = Keys.O;

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

            IndirectLightBrightness.MinusKey = Keys.D9;
            IndirectLightBrightness.PlusKey = Keys.D0;
            IndirectLightBrightness.Min = 0f;
            IndirectLightBrightness.Max = 4.0f;
            IndirectLightBrightness.Speed = 0.333333f;

            BounceDistance.MinusKey = Keys.OemMinus;
            BounceDistance.PlusKey = Keys.OemPlus;
            BounceDistance.Min = 128f;
            BounceDistance.Max = 1024f;
            BounceDistance.Speed = 128f;

            ProbeInterval.MinusKey = Keys.OemComma;
            ProbeInterval.PlusKey = Keys.OemPeriod;
            ProbeInterval.Min = 32f;
            ProbeInterval.Max = 128f;
            ProbeInterval.Speed = 8f;

            GISourceBounce.MinusKey = Keys.OemSemicolon;
            GISourceBounce.PlusKey = Keys.OemQuotes;
            GISourceBounce.Min = 0f;
            GISourceBounce.Max = BounceCount - 1;
            GISourceBounce.Speed = 1f;

            LightScaleFactor.MinusKey = Keys.Q;
            LightScaleFactor.PlusKey = Keys.W;
            LightScaleFactor.Min = 0.5f;
            LightScaleFactor.Max = 6.0f;
            LightScaleFactor.Speed = 0.5f;

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
            Environment.ZToYMultiplier = 2.5f;

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width, Height, true, false, enableGlobalIllumination: true,
                    maximumGIProbeCount: 2048, giProbeQualityLevel: GIProbeQualityLevels.High,
                    maximumGIBounceCount: BounceCount
                ) {
                    MaxFieldUpdatesPerFrame = 3,
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

            MovableLight = new SphereLightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 0.2f, 0.2f, 0.5f),
                Radius = 240,
                RampLength = 60,
                RampMode = LightSourceRampMode.Exponential,
                AmbientOcclusionOpacity = AOOpacity
            };

            Environment.Lights.Add(MovableLight);

            DirectionalQuality = new RendererQualitySettings {
                MinStepSize = 2f,
                LongStepFactor = 0.95f,
                MaxConeRadius = 24,
                OcclusionToOpacityPower = 0.7f                
            };

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-0.75f, -0.7f, -0.2f),
                Color = new Vector4(0.1f, 0.0f, 0.8f, 0.6f),
                Quality = DirectionalQuality,
                AmbientOcclusionOpacity = AOOpacity
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.5f, -0.7f, -0.3f),
                Color = new Vector4(0.1f, 0.8f, 0.0f, 0.6f),
                Quality = DirectionalQuality,
                AmbientOcclusionOpacity = AOOpacity
            });

            if (false)
            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-1f, -0.1f, -0.1f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Quality = DirectionalQuality
            });

            Rect(new Vector2(330, 300), new Vector2(Width, 340), 0f, 60f);

            for (int i = 0; i < 7; i++)
                Pillar(new Vector2(10 + (i * 220), 500), 0.8f);

            Rect(new Vector2(630, 650), new Vector2(Width, 690), 0f, 40f);
            Rect(new Vector2(630, 650), new Vector2(670, Height - 40), 0f, 40f);
            Rect(new Vector2(630, 790), new Vector2(800, 830), 0f, 40f);
            Rect(new Vector2(900, 790), new Vector2(Width, 830), 0f, 40f);
            Rect(new Vector2(630, 930), new Vector2(900, 970), 0f, 40f);
            Rect(new Vector2(1000, 930), new Vector2(Width - 100, 970), 0f, 40f);
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Environment.GIProbeOffset = new Vector3(ProbeInterval / 2f, ProbeInterval / 2f, 35);
            Environment.GIProbeInterval = new Vector2(ProbeInterval, ProbeInterval);

            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;
            Renderer.Configuration.RenderScale = Vector2.One * LightmapScaleRatio;
            Renderer.Configuration.RenderSize = new Pair<int>(
                (int)(Renderer.Configuration.MaximumRenderSize.First * LightmapScaleRatio),
                (int)(Renderer.Configuration.MaximumRenderSize.Second * LightmapScaleRatio)
            );
            Renderer.Configuration.GIBlendMode = AdditiveIndirectLight ? RenderStates.AdditiveBlend : RenderStates.MaxBlend;
            Renderer.Configuration.GIBounceSearchDistance = BounceDistance.Value;

            foreach (var ls in Environment.Lights)
                ls.AmbientOcclusionRadius = EdgeShadows ? AORadius : 0.0f;

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

                var lighting = Renderer.RenderLighting(
                    bg, 1, 1.0f / LightScaleFactor, 
                    RenderDirectLight,
                    new GIRenderSettings {
                        BounceIndex = (int)GISourceBounce.Value,
                        Brightness = IndirectLightBrightness
                    }
                );
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

                if (ShowProbeSH)
                    Renderer.VisualizeGIProbes(
                        group, 2, ProbeInterval.Value * 0.48f, 
                        bounceIndex: (int)GISourceBounce.Value, 
                        brightness: ProbeVisBrightness
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
                const float step = 0.1f;
                
                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                MovableLight.Position = mousePos;
                MovableLight.Opacity = EnablePointLight ? 1 : 0;
                MovableLight.Color.W = Arithmetic.Pulse((float)Time.Seconds / 4f, 0.7f, 0.9f);
                MovableLight.CastsShadows = EnableShadows;

                foreach (var d in Environment.Lights.OfType<DirectionalLightSource>()) {
                    d.Opacity = EnableDirectionalLights ? 1 : 0;
                    d.CastsShadows = EnableShadows;
                }
            }
        }
    }
}
