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
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
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
                Color = new Vector4(0.66f, 0, 0, 1),
                RampStart = 20,
                RampEnd = 160
            });

            Environment.LightSources.Add(new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(0, 0, 0.66f, 1),
                RampStart = 40,
                RampEnd = 250
            });

            Environment.LightSources.Add(new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(0, 0.66f, 0, 1),
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
                ),
                new LightObstruction(
                    new Vector2(256, 16),
                    new Vector2(512, 256)
                )
            });
        }

        protected override void Update (GameTime gameTime) {
            var ms = Mouse.GetState();
            var mousePos = new Vector2(ms.X, ms.Y);

            var angle = gameTime.TotalGameTime.TotalSeconds * 2f;
            const float radius = 64f;

            Environment.LightSources[0].Position = mousePos;

            if (Environment.LightSources.Count > 1)
                Environment.LightSources[1].Position = mousePos + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);

            if (Environment.LightSources.Count > 2)
                Environment.LightSources[2].Position = mousePos + new Vector2((float)Math.Cos(angle + 1f) * radius, (float)Math.Sin(angle + 1f) * radius);

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 0, Materials.Clear, clearColor: Color.Black);

            Renderer.RenderLighting(frame, 1);

            Renderer.RenderOutlines(frame, 2);
        }
    }
}
