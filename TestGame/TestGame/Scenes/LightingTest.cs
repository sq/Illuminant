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

namespace TestGame.Scenes {
    public class LightingTest : Scene {
        DefaultMaterialSet LightmapMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        bool ShowOutlines = true;

        LightObstructionLine Dragging = null;

        private readonly List<float> LuminanceSamples = new List<float>();

        private readonly List<long> TickCountSamples = new List<long>();
        private readonly List<float> RollingAverageSamples = new List<float>();

        public const int RollingLength = 100;

        private float MaximumLuminance = 5.0f;
        private float LuminanceScaler = 8.25f;
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

            var light = new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(1f, 1f, 1f, 1),
                RampStart = 50,
                RampEnd = 275,
            };

            Lights.Add(light);
            Environment.LightSources.Add(light);

            var rng = new Random(1234);
            for (var i = 0; i < 25; i++) {
                light = new LightSource {
                    Position = new Vector2(64, 64),
                    Color = new Vector4((float)rng.NextDouble(0.1f, 1.0f), (float)rng.NextDouble(0.1f, 1.0f), (float)rng.NextDouble(0.1f, 1.0f), 1.0f),
                    RampStart = rng.NextFloat(24, 40),
                    RampEnd = rng.NextFloat(140, 160),
                    RampMode = LightSourceRampMode.Exponential
                };

                Lights.Add(light);
                Environment.LightSources.Add(light);
            }

            const int spiralCount = 1800;
            float spiralRadius = 0, spiralRadiusStep = 330f / spiralCount;
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

        private void ComputeAverageLuminance () {
            const int stepSize = 20;
            const float stepSizeF = stepSize;
            int numSteps = Height / stepSize;
            const float offsetStep = stepSizeF * 4;

            LuminanceSamples.Clear();

            var query = new LightingQuery(Environment);

            var tp = new Squared.Util.Win32TimeProvider();
            long startTicks = tp.Ticks;

            Parallel.For(
                0, numSteps,
                () => new List<float>(),
                (yStep, state, samples) => {
                    float y = yStep * stepSizeF;
                    float xOffset = (y % offsetStep) / 8f;
                    float localWidth = Width + xOffset;

                    for (Vector2 position = new Vector2(xOffset, y); position.X < localWidth; position.X += stepSizeF) {
                        var sample = query.ComputeReceivedLightAtPosition(position);
                        var sampleLuminance = (sample.X * 0.299f) + (sample.Y * 0.587f) + (sample.Z * 0.114f);

                        samples.Add(sampleLuminance);
                    }


                    return samples;
                },
                (samples) => {
                    lock (LuminanceSamples)
                        LuminanceSamples.AddRange(samples);
                }
            );

            long endTicks = tp.Ticks;
            TickCountSamples.Add(endTicks - startTicks);
            if (TickCountSamples.Count > RollingLength)
                TickCountSamples.RemoveAt(0);

            LuminanceSamples.Sort();

            int outliersToSkip = (LuminanceSamples.Count * 4) / 100;

            float accumulator = 0;
            int count = 0;

            for (int i = outliersToSkip, e = Math.Max(LuminanceSamples.Count - outliersToSkip - 1, 0); i < e; i++) {
                accumulator += LuminanceSamples[i];
                count += 1;
            }

            AverageLuminance = (accumulator / count) * LuminanceScaler;

            RollingAverageSamples.Add(AverageLuminance);

            if (RollingAverageSamples.Count > RollingLength)
                RollingAverageSamples.RemoveAt(0);
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

            var args = new float[] {
                MagnitudeScale, MiddleGray, AppliedAverageLuminance, MaximumLuminance
            };

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                (dm, _) =>
                    Renderer.IlluminantMaterials.SetGammaCompressionParameters(args[0], args[1], args[2], args[3])
            )) {
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
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.Q))
                    MiddleGray -= step;
                else if (KeyWasPressed(Keys.W))
                    MiddleGray += step;

                if (KeyWasPressed(Keys.A))
                    LuminanceScaler -= step;
                else if (KeyWasPressed(Keys.S))
                    LuminanceScaler += step;

                if (MiddleGray < 0)
                    MiddleGray = 0;
                if (LuminanceScaler < 0)
                    LuminanceScaler = 0;

                if (KeyWasPressed(Keys.O))
                    ShowOutlines = !ShowOutlines;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                var mousePos = new Vector2(ms.X, ms.Y);

                var angle = gameTime.TotalGameTime.TotalSeconds * 0.125f;
                const float radius = 225f;

                Lights[0].Position = mousePos;

                float stepOffset = (float)((Math.PI * 2) / (Environment.LightSources.Count - 1));
                float offset = 0;
                for (int i = 1; i < Environment.LightSources.Count; i++, offset += stepOffset) {
                    float localRadius = (float)(radius + (radius * Math.Sin(offset * 4f) * 0.5f));
                    Lights[i].Position = mousePos + new Vector2((float)Math.Cos(angle + offset) * localRadius, (float)Math.Sin(angle + offset) * localRadius);
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

            ComputeAverageLuminance();
        }

        public float AppliedAverageLuminance {
            get {
                return RollingAverageSamples.Average();
            }
        }

        public override string Status {
            get { 
                return String.Format(
                    "Current Avg={0:00.00} | Applied Avg={1:00.00} | Scaler = {2:000.0} | MS/lighting update = {3:000.00}",
                    AverageLuminance, AppliedAverageLuminance, LuminanceScaler, TimeSpan.FromTicks((long)TickCountSamples.Average()).TotalMilliseconds
                ); 
            }
        }
    }
}
