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
    public class DitheringTest : Scene {
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        Toggle sRGB, ExponentialRamp;
        Slider DitheringStrength, DitheringPower;

        public DitheringTest (TestGame game, int width, int height)
            : base(game, width, height) {

            DitheringStrength.Value = 1f;
            DitheringPower.Value = 8;

            sRGB.Key = Keys.S;

            DitheringStrength.Max = 1;
            DitheringStrength.Min = 0;
            DitheringStrength.Speed = 0.1f;

            DitheringPower.Max = 12;
            DitheringPower.Min = 1;
            DitheringPower.Speed = 1;
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
                }
            );

            Environment.Lights.Add(new SphereLightSource {
                Position = new Vector3(0, Height / 2f, 0),
                Radius = 4,
                RampLength = Width * 0.9f,
                Color = Vector4.One
            });
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

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
                            Strength = DitheringStrength,
                            Power = (int)DitheringPower.Value
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
