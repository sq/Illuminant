using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Squared.Illuminant;
using Squared.Render;

namespace TestGame.Scenes {
    public class LightingTest : Scene {
        DefaultMaterialSet LightmapMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        public readonly List<LightSource> Lights = new List<LightSource>();

        LightObstructionLine Dragging = null;

        public LightingTest (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        public override void LoadContent () {
            LightmapMaterials = new DefaultMaterialSet(Game.Services);

            // Since the spiral is very detailed
            LightingEnvironment.DefaultSubdivision = 128f;

            Environment = new LightingEnvironment();

            Renderer = new LightingRenderer(Game.Content, LightmapMaterials, Environment);

            Environment.LightSources.Add(new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(0.6f, 0.6f, 0.6f, 1),
                RampStart = 40,
                RampEnd = 256
            });

            var rng = new Random();
            for (var i = 0; i < 6; i++) {
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

        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, Width,
                Height, 0,
                0, 1
            );

            ClearBatch.AddNew(frame, 0, LightmapMaterials.Clear, clearColor: new Color(0, 0, 0, 255), clearZ: 0, clearStencil: 0);

            Renderer.RenderLighting(frame, frame, 1);
            Renderer.RenderOutlines(frame, 2, true);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
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
        }
    }
}
