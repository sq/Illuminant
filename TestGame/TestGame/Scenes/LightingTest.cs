using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Illuminant;
using Squared.Render;

namespace TestGame.Scenes {
    public class LightingTest : Scene {
        DefaultMaterialSet LightmapMaterials;

        LightReceiver[] Receivers;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        bool ShowOutlines = true;

        LightObstructionLine Dragging = null;

        private float MaximumLuminance = 4.5f;
        private float AverageLuminance = 0.1f;
        private float MiddleGray = 0.6f;
        private float MagnitudeScale = 5f;

        public LightingTest (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        private void CreateRenderTargets () {
            int scaledWidth = (int)Width;
            int scaledHeight = (int)Height;

            const int multisampleCount = 0;

            if (scaledWidth < 4)
                scaledWidth = 4;
            if (scaledHeight < 4)
                scaledHeight = 4;

            if ((Lightmap == null) || (scaledWidth != Lightmap.Width) || (scaledHeight != Lightmap.Height)) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, scaledWidth, scaledHeight, false,
                    SurfaceFormat.Rgba64, DepthFormat.Depth24Stencil8, multisampleCount, RenderTargetUsage.DiscardContents
                );
            }
        }

        public override void LoadContent () {
            LightmapMaterials = new DefaultMaterialSet(Game.Services);

            // Since the spiral is very detailed
            LightingEnvironment.DefaultSubdivision = 128f;

            Environment = new LightingEnvironment();

            Renderer = new LightingRenderer(Game.Content, LightmapMaterials, Environment);

            Environment.LightSources.Add(new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(1f, 1f, 1f, 1),
                RampStart = 40,
                RampEnd = 256
            });

            var rng = new Random();
            for (var i = 0; i < 33; i++) {
                const float opacity = 0.7f;
                Environment.LightSources.Add(new LightSource {
                    Position = new Vector2(64, 64),
                    Color = new Vector4((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), opacity),
                    RampStart = 10,
                    RampEnd = 120
                });
            }

            Environment.Obstructions.Add(
                new LightObstructionLineStrip(
                    new Vector2(16, 16),
                    new Vector2(256, 16),
                    new Vector2(256, 256),
                    new Vector2(16, 256),
                    new Vector2(16, 16)
                )
            );

            Receivers = new[] {
                Environment.AddLightReceiver(new Vector2(64, 64)),
                Environment.AddLightReceiver(new Vector2(192, 192)),
                Environment.AddLightReceiver(new Vector2(64, 192)),
                Environment.AddLightReceiver(new Vector2(192, 64))
            };

            const int spiralCount = 2048;
            float spiralRadius = 0, spiralRadiusStep = 360f / spiralCount;
            float spiralAngle = 0, spiralAngleStep = (float)(Math.PI / (spiralCount / 36f));
            Vector2 previous = default(Vector2);

            for (int i = 0; i < spiralCount; i++, spiralAngle += spiralAngleStep, spiralRadius += spiralRadiusStep) {
                var current = new Vector2(
                    (float)(Math.Cos(spiralAngle) * spiralRadius) + (Width / 2f),
                    (float)(Math.Sin(spiralAngle) * spiralRadius) + (Height / 2f)
                );

                if (i > 0) {
                    Environment.Obstructions.Add(new LightObstructionLine(
                        previous, current
                    ));
                }

                previous = current;
            }
        }

        private void SetGammaCompressionParameters (DeviceManager device, object userData) {
            Renderer.IlluminantMaterials.SetGammaCompressionParameters(MagnitudeScale, MiddleGray, AverageLuminance, MaximumLuminance);
        }

        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, Width,
                Height, 0,
                0, 1
            );

            CreateRenderTargets();

            using (var bg = BatchGroup.ForRenderTarget(frame, -1, Lightmap, after: SetGammaCompressionParameters)) {
                ClearBatch.AddNew(bg, 0, LightmapMaterials.Clear, clearColor: Color.Black, clearZ: 0, clearStencil: 0);

                Renderer.RenderLighting(frame, bg, 1, intensityScale: 1 / MagnitudeScale);
            };

            ClearBatch.AddNew(frame, 0, Game.ScreenMaterials.Clear, clearColor: Color.Black);

            using (var bb = BitmapBatch.New(
                frame, 1, 
                Game.ScreenMaterials.Get(Renderer.IlluminantMaterials.ScreenSpaceGammaCompressedBitmap, blendState: BlendState.Opaque)
            ))
                bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));

            if (ShowOutlines)
                Renderer.RenderOutlines(frame, 2, true);

            using (var gb = GeometryBatch.New(frame, 3, Game.ScreenMaterials.Get(Game.ScreenMaterials.ScreenSpaceGeometry, blendState: BlendState.Opaque)))
            for (var i = 0; i < Receivers.Length; i++) {
                var r = Receivers[i];
                var size = new Vector2(8, 8);
                var bounds = new Bounds(r.Position - size, r.Position + size);
                var color = new Color(r.ReceivedLight.X, r.ReceivedLight.Y, r.ReceivedLight.Z, 1.0f) * r.ReceivedLight.W;

                // Console.WriteLine("Receiver {0} at {1}: {2}", i, r.Position, r.ReceivedLight);

                gb.AddFilledQuad(bounds, color);
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.Q))
                    MiddleGray -= step;
                else if (KeyWasPressed(Keys.W))
                    MiddleGray += step;

                if (KeyWasPressed(Keys.A))
                    AverageLuminance -= step;
                else if (KeyWasPressed(Keys.S))
                    AverageLuminance += step;

                if (MiddleGray < 0)
                    MiddleGray = 0;
                if (AverageLuminance < 0)
                    AverageLuminance = 0;

                if (KeyWasPressed(Keys.O))
                    ShowOutlines = !ShowOutlines;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                var mousePos = new Vector2(ms.X, ms.Y);

                var angle = gameTime.TotalGameTime.TotalSeconds * 2f;
                const float radius = 200f;

                Environment.LightSources[0].Position = mousePos;

                float stepOffset = (float)((Math.PI * 2) / (Environment.LightSources.Count - 1));
                float offset = 0;
                for (int i = 1; i < Environment.LightSources.Count; i++, offset += stepOffset) {
                    float localRadius = (float)(radius + (radius * Math.Sin(offset * 4f) * 0.5f));
                    Environment.LightSources[i].Position = mousePos + new Vector2((float)Math.Cos(angle + offset) * localRadius, (float)Math.Sin(angle + offset) * localRadius);
                }

                if (ms.LeftButton == ButtonState.Pressed) {
                    if (Dragging == null) {
                        Environment.Obstructions.Add(Dragging = new LightObstructionLine(mousePos, mousePos));
                    } else {
                        Dragging.B = mousePos;
                    }
                } else {
                    if (Dragging != null) {
                        Dragging.B = mousePos;
                        Dragging = null;
                    }
                }
            }

            Environment.UpdateReceivers();
        }
    }
}
