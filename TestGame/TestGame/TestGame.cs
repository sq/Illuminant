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

        public TestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferWidth = 1280;
            Graphics.PreferredBackBufferHeight = 720;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = true;

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
                Color = new Vector4(1, 0, 0, 1),
                RampStart = 20,
                RampEnd = 160
            });

            Environment.LightSources.Add(new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(0, 0, 1, 0.66f),
                RampStart = 40,
                RampEnd = 250
            });

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
        }

        protected override void Update (GameTime gameTime) {
            var ms = Mouse.GetState();
            var mousePos = new Vector2(ms.X, ms.Y);

            var angle = gameTime.TotalGameTime.TotalSeconds * 2f;
            const float radius = 32f;

            Environment.LightSources[0].Position = mousePos;
            Environment.LightSources[1].Position = mousePos + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 0, Color.Black, Materials.Clear);

            Renderer.RenderLighting(frame, 1);

            Renderer.RenderOutlines(frame, 2);
        }
    }
}
