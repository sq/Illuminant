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
    public class ClipPlaneTest : Scene {
        DefaultMaterialSet LightmapMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        bool ShowOutlines = true;

        LightObstructionLine Dragging = null;

        public ClipPlaneTest (TestGame game, int width, int height)
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
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, multisampleCount, RenderTargetUsage.DiscardContents
                );
            }
        }

        public override void LoadContent () {
            LightmapMaterials = new DefaultMaterialSet(Game.Services);

            // Since the spiral is very detailed
            LightingEnvironment.DefaultSubdivision = 128f;

            Environment = new LightingEnvironment();

            Renderer = new LightingRenderer(Game.Content, Game.RenderCoordinator, LightmapMaterials, Environment);

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
                    RampStart = rng.NextFloat(24, 60),
                    RampEnd = rng.NextFloat(130, 210),
                    RampMode = LightSourceRampMode.Exponential
                };

                Lights.Add(light);
                Environment.LightSources.Add(light);
            }

            const int spiralCount = 1400;
            float spiralRadius = 0, spiralRadiusStep = 550f / spiralCount;
            float spiralAngle = 0, spiralAngleStep = (float)(Math.PI / (spiralCount / 12f));
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
        
        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, Width,
                Height, 0,
                0, 1
            );

            CreateRenderTargets();

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap
            )) {
                ClearBatch.AddNew(bg, 0, LightmapMaterials.Clear, clearColor: Color.Black, clearZ: 0, clearStencil: 0);

                Renderer.RenderLighting(frame, bg, 1, intensityScale: 1);
            };

            ClearBatch.AddNew(frame, 0, Game.ScreenMaterials.Clear, clearColor: Color.Black);

            using (var bb = BitmapBatch.New(
                frame, 1,
                Game.ScreenMaterials.Get(Game.ScreenMaterials.ScreenSpaceBitmap, blendState: BlendState.Opaque)
            ))
                bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));

            if (ShowOutlines)
                Renderer.RenderOutlines(frame, 2, true);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.O))
                    ShowOutlines = !ShowOutlines;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
                
                var mousePos = new Vector2(ms.X, ms.Y);
                var mousePos3 = new Vector3(mousePos, 0);

                var clipRegionSize = new Vector3(64, 64, 1);
                var clipRegion = new Bounds3(
                    mousePos3 - clipRegionSize,
                    mousePos3 + clipRegionSize
                );

                Environment.ClipRegion = clipRegion;

                var angle = gameTime.TotalGameTime.TotalSeconds * 0.125f;
                const float radius = 320f;

                var lightCenter = new Vector2(Width / 2, Height / 2);

                Lights[0].Position = lightCenter;

                float stepOffset = (float)((Math.PI * 2) / (Environment.LightSources.Count - 1));
                float offset = 0;
                for (int i = 1; i < Environment.LightSources.Count; i++, offset += stepOffset) {
                    float localRadius = (float)(radius + (radius * Math.Sin(offset * 4f) * 0.5f));
                    Lights[i].Position = lightCenter + new Vector2((float)Math.Cos(angle + offset) * localRadius, (float)Math.Sin(angle + offset) * localRadius);
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
        }

        public override string Status {
	        get { return ""; }
        }
    }
}
