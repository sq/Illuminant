using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Illuminant;
using Squared.Render;

namespace TestGame {
    public class TestGame : MultithreadedGame {
        GraphicsDeviceManager Graphics;
        DefaultMaterialSet Materials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        bool ShowOutlines, ShowLights;

        bool Dragging;
        Vector2 DragStart;

        public TestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1280;
            Graphics.PreferredBackBufferHeight = 720;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = false;

            Content.RootDirectory = "Content";

            UseThreadedDraw = true;
            IsFixedTimeStep = false;
        }

        protected override void LoadContent () {
            base.LoadContent();

            Materials = new DefaultMaterialSet(Content) {
                ViewportScale = new Vector2(1, 1),
                ViewportPosition = new Vector2(0, 0),
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0, GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, 0,
                    0, 1
                )
            };

            Environment = new LightingEnvironment();
            Renderer = new LightingRenderer(Content, Materials, Environment);

            Environment.LightSources.Add(new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(0.6f, 0.6f, 0.6f, 1),
                RampStart = 40,
                RampEnd = 256
            });

            var rng = new Random();
            for (var i = 0; i < 65; i++) {
                const float opacity = 0.6f;
                Environment.LightSources.Add(new LightSource {
                    Position = new Vector2(64, 64),
                    Color = new Vector4((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble(), opacity),
                    RampStart = 10,
                    RampEnd = 120
                });
            }

            Environment.Obstructions.AddRange(new[] {
                new LightObstruction(
                    new Vector2(16, 16),
                    new Vector2(256, 16)
                ),
                new LightObstruction(
                    new Vector2(16, 16),
                    new Vector2(16, 256)
                ),
                new LightObstruction(
                    new Vector2(256, 16),
                    new Vector2(256, 256)
                ),
                new LightObstruction(
                    new Vector2(16, 256),
                    new Vector2(256, 256)
                )
            });

            const int spiralCount = 10240;
            float spiralRadius = 0, spiralRadiusStep = 360f / spiralCount;
            float spiralAngle = 0, spiralAngleStep = (float)(Math.PI / (spiralCount / 36f));
            Vector2 previous = default(Vector2);

            for (int i = 0; i < spiralCount; i++, spiralAngle += spiralAngleStep, spiralRadius += spiralRadiusStep) {
                var current = new Vector2(
                    (float)(Math.Cos(spiralAngle) * spiralRadius) + (Graphics.PreferredBackBufferWidth / 2f),
                    (float)(Math.Sin(spiralAngle) * spiralRadius) + (Graphics.PreferredBackBufferHeight / 2f)
                );

                if (i > 0) {
                    Environment.Obstructions.Add(new LightObstruction(
                        previous, current
                    ));
                }

                previous = current;
            }
        }

        protected override void Update (GameTime gameTime) {
            var ks = Keyboard.GetState();
            ShowOutlines = ks.IsKeyDown(Keys.O);

            var ms = Mouse.GetState();
            var mousePos = new Vector2(ms.X, ms.Y);

            Materials.ViewportScale = new Vector2((float)(1.0 + (ms.ScrollWheelValue / 500f)));

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
                if (!Dragging) {
                    Dragging = true;
                    DragStart = mousePos;
                }
            } else {
                if (Dragging) {
                    Environment.Obstructions.Add(new LightObstruction(
                        DragStart, mousePos
                    ));
                    Dragging = false;
                }
            }

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 0, Materials.Clear, clearColor: Color.Black);

            Renderer.RenderLighting(frame, 1);

            if (ShowOutlines)
                Renderer.RenderOutlines(frame, 2, ShowLights);
        }
    }
}
