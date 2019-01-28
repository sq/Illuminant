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
    public class DitheringTest : Scene {
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        Toggle sRGB, ExponentialRamp;
        Slider
            Strength,
            Power,
            BandSize,
            RangeMin,
            RangeMax,
            LightSize;

        public DitheringTest (TestGame game, int width, int height)
            : base(game, width, height) {

            Strength.Value = 1;
            Power.Value = 1;
            BandSize.Value = 1;
            RangeMax.Value = 1;
            LightSize.Value = 0.95f;

            sRGB.Key = Keys.S;

            Power.Max = 12;
            Power.Min = 1;
            Power.Speed = 1;
            Power.Integral = true;

            LightSize.Speed = 0.05f;
            LightSize.Max = 2.0f;

            InitUnitSlider(Strength, BandSize, RangeMax, RangeMin);
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
                    SurfaceFormat.Color, DepthFormat.None, 0, 
                    RenderTargetUsage.PlatformContents
                );
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
                    Width, Height, true, false
                ) {
                    MaximumFieldUpdatesPerFrame = 3,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    }
                }, Game.IlluminantMaterials
            );

            Environment.Lights.Add(new SphereLightSource {
                Position = new Vector3(Width / 2f, Height / 2f, 0),
                Radius = 4,
                Color = Vector4.One * 1f
            });
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            var l = Environment.Lights.OfType<SphereLightSource>().First();
            l.RampLength = Width * LightSize;
            l.Position.X = Width * (1 - LightSize);

            Renderer.UpdateFields(frame, -2);
            Environment.Lights.OfType<SphereLightSource>().First().RampMode =
                ExponentialRamp
                    ? LightSourceRampMode.Exponential
                    : LightSourceRampMode.Linear;

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

                var lighting = Renderer.RenderLighting(bg, 1, 1 / 2f);
                lighting.Resolve(
                    bg, 2, Width, Height, 
                    hdr: new HDRConfiguration {
                        InverseScaleFactor = 2f,
                        Gamma = sRGB ? 2.3f : 1.0f,
                        ResolveToSRGB = sRGB,
                        Dithering = new DitheringSettings {
                            Strength = Strength,
                            Power = (int)Power.Value,
                            BandSize = BandSize,
                            RangeMin = RangeMin,
                            RangeMax = RangeMax
                        },
                    }
                );
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                using (var bb = BitmapBatch.New(
                    group, 1,
                    Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                    samplerState: SamplerState.LinearClamp
                ))
                    bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;
                
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;
            }
        }
    }
}
