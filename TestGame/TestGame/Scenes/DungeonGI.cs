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
    public class DungeonGI : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        float LightZ;

        const int BounceCount = 3;
        const int MultisampleCount = 0;
        const int MaxStepCount = 128;
        const float AORadius = 6;
        const float AOOpacity = 0.15f;
        const float ProbeZ = 1;
        const float ProbeVisBrightness = 1.1f;

        Toggle ShowGBuffer,
            ShowDistanceField,
            RenderDirectLight,
            EnableShadows,
            ShowProbeSH,
            AdditiveIndirectLight,
            sRGB;

        Slider DistanceFieldResolution,
            LightmapScaleRatio,
            IndirectLightBrightness,
            BounceDistance,
            ProbeInterval,
            GIBounceCount,
            LightScaleFactor;

        public DungeonGI (TestGame game, int width, int height)
            : base(game, width, height) {

            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;
            RenderDirectLight.Value = true;
            ShowProbeSH.Value = false;
            EnableShadows.Value = true;
            IndirectLightBrightness.Value = 1.0f;
            AdditiveIndirectLight.Value = true;
            BounceDistance.Value = 512;
            ProbeInterval.Value = 48;
            GIBounceCount.Value = 1;
            LightScaleFactor.Value = 2.5f;

            ShowGBuffer.Key = Keys.G;
            ShowDistanceField.Key = Keys.D;
            RenderDirectLight.Key = Keys.L;
            ShowProbeSH.Key = Keys.P;
            EnableShadows.Key = Keys.S;
            AdditiveIndirectLight.Key = Keys.A;
            sRGB.Key = Keys.R;

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

            GIBounceCount.MinusKey = Keys.OemSemicolon;
            GIBounceCount.PlusKey = Keys.OemQuotes;
            GIBounceCount.Min = 0f;
            GIBounceCount.Max = BounceCount;
            GIBounceCount.Speed = 1f;

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
                    enableBrightnessEstimation: false, 
                    enableGlobalIllumination: true,
                    maximumGIProbeCount: 2048, giProbeQualityLevel: GIProbeSampleCounts.High,
                    maximumGIBounceCount: BounceCount
                ) {
                    MaximumFieldUpdatesPerFrame = 3,
                    MaximumGIUpdatesPerFrame = 1,
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
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.Configuration.RenderScale = Vector2.One * LightmapScaleRatio;
            Renderer.Configuration.RenderSize = new Pair<int>(
                (int)(Renderer.Configuration.MaximumRenderSize.First * LightmapScaleRatio),
                (int)(Renderer.Configuration.MaximumRenderSize.Second * LightmapScaleRatio)
            );
            Renderer.Configuration.GIBlendMode = AdditiveIndirectLight ? RenderStates.AdditiveBlend : RenderStates.MaxBlend;
            Renderer.Configuration.GIBounceSearchDistance = BounceDistance.Value;

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

                var girs = new GIRenderSettings {
                    BounceIndex = (int)GIBounceCount.Value - 1,
                    Brightness = IndirectLightBrightness
                };
                if (GIBounceCount.Value == 0)
                    girs = null;

                var lighting = Renderer.RenderLighting(
                    bg, 1, 1.0f / LightScaleFactor, 
                    RenderDirectLight,
                    girs
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

                if (ShowProbeSH && (GIBounceCount.Value > 0))
                    Renderer.VisualizeGIProbes(
                        group, 2, ProbeInterval.Value * 0.4f, 
                        bounceIndex: (int)GIBounceCount.Value - 1, 
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
            }
        }
    }
}
