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
using Squared.Game;
using Squared.Illuminant;
using Squared.Render;

namespace TestGame {
    public class TestGame : MultithreadedGame {
        GraphicsDeviceManager Graphics;
        DefaultMaterialSet Materials;

        LightingEnvironment ForegroundEnvironment, BackgroundEnvironment;
        LightingRenderer ForegroundRenderer, BackgroundRenderer;

        Texture2D[] Layers = new Texture2D[5];
        RenderTarget2D BackgroundLightmap, ForegroundLightmap;

        bool[,] ForegroundTiles = new bool[,] {
            { true, true, true, true, true, false, true, true, true },
            { true, true, false, true, true, false, true, true, false },
            { true, true, false, false, false, false, false, true, false },
            { true, true, true, true, false, false, false, false, false },
            { true, true, true, true, false, false, true, true, false },
            { true, true, true, true, true, false, true, true, false },
            { true, true, true, true, false, false, true, true, false },
            { true, true, false, true, true, false, true, true, false },
            { true, true, false, false, false, false, false, true, false }
        };

        bool ShowOutlines, ShowLightmap;

        float ViewScale = 1;

        LightObstruction Dragging = null;

        public TestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1257;
            Graphics.PreferredBackBufferHeight = 1250;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = false;

            Content.RootDirectory = "Content";

            UseThreadedDraw = true;
            IsFixedTimeStep = false;
        }

        private void AddTorch (float x, float y) {
            var torch = new LightSource {
                Mode = LightSourceMode.Alpha,
                Position = new Vector2(x, y),
                Color = new Vector4(255 / 255.0f, 158 / 255.0f, 0f, 0.8f),
                RampStart = 40,
                RampEnd = 350
            };

            ForegroundEnvironment.LightSources.Add(torch);
        }

        private void AddAmbientLight (float x, float y) {
            var ambient = new LightSource {
                Mode = LightSourceMode.Max,
                Position = new Vector2(x, y),
                Color = new Vector4(1, 1, 1, 0.45f),
                RampStart = 2000,
                RampEnd = 2500
            };

            BackgroundEnvironment.LightSources.Add(ambient);
        }

        private void GenerateObstructionsFromTiles () {
            const float xOffset = 38, yOffset = 34;
            const float xTileSize = 128, yTileSize = 128;

            for (var y = 0; y < ForegroundTiles.GetLength(0); y++) {
                float yPos = (y * yTileSize) + yOffset;
                for (var x = 0; x < ForegroundTiles.GetLength(1); x++) {
                    float xPos = (x * xTileSize) + xOffset;

                    if (!ForegroundTiles[y, x])
                        continue;

                    var bounds = new Bounds(
                        new Vector2(xPos, yPos),
                        new Vector2(xPos + xTileSize, yPos + yTileSize)
                    );

                    BackgroundEnvironment.Obstructions.Add(new LightObstruction(
                        bounds.TopLeft, bounds.TopRight
                    ));
                    BackgroundEnvironment.Obstructions.Add(new LightObstruction(
                        bounds.TopRight, bounds.BottomRight
                    ));
                    BackgroundEnvironment.Obstructions.Add(new LightObstruction(
                        bounds.BottomRight, bounds.BottomLeft
                    ));
                    BackgroundEnvironment.Obstructions.Add(new LightObstruction(
                        bounds.BottomLeft, bounds.TopLeft
                    ));
                }
            }
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

            BackgroundLightmap = new RenderTarget2D(
                GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, false, 
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.DiscardContents
            );

            ForegroundLightmap = new RenderTarget2D(
                GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, false,
                SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.DiscardContents
            );

            // Since the spiral is very detailed
            LightingEnvironment.DefaultSubdivision = 128f;

            BackgroundEnvironment = new LightingEnvironment();
            ForegroundEnvironment = new LightingEnvironment();

            BackgroundRenderer = new LightingRenderer(Content, Materials, BackgroundEnvironment);
            ForegroundRenderer = new LightingRenderer(Content, Materials, ForegroundEnvironment);

            for (float x = -200; x < 1400; x += 200) {
                AddAmbientLight(x, -200);
            }

            AddTorch(102, 132);
            AddTorch(869, 132);
            AddTorch(102, 646);
            AddTorch(869, 645);

            GenerateObstructionsFromTiles();

            Layers[0] = Content.Load<Texture2D>("layers_bg");
            Layers[1] = Content.Load<Texture2D>("layers_bricks");
            Layers[2] = Content.Load<Texture2D>("layers_fg");
            Layers[3] = Content.Load<Texture2D>("layers_chars");
            Layers[4] = Content.Load<Texture2D>("layers_torches");
        }

        protected override void Update (GameTime gameTime) {
            var ks = Keyboard.GetState();
            ShowOutlines = ks.IsKeyDown(Keys.O);
            ShowLightmap = ks.IsKeyDown(Keys.L);

            var ms = Mouse.GetState();
            IsMouseVisible = true;
            ViewScale = (float)(1.0 + (ms.ScrollWheelValue / 500f));
            var mousePos = new Vector2(ms.X, ms.Y) / ViewScale;

            if (ms.LeftButton == ButtonState.Pressed) {
                if (Dragging == null) {
                    BackgroundEnvironment.Obstructions.Add(Dragging = new LightObstruction(mousePos, mousePos));
                } else {
                    Dragging.B = mousePos;
                }
            } else {
                if (Dragging != null) {
                    Dragging.B = mousePos;
                    Dragging = null;
                }
            }

            base.Update(gameTime);
        }

        private void ResetViewScale (DeviceManager device) {
            Materials.ViewportScale = new Vector2(1, 1);
            Materials.ApplyShaderVariables();
        }

        private void ApplyViewScale (DeviceManager device) {
            Materials.ViewportScale = new Vector2(ViewScale);
            Materials.ApplyShaderVariables();
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            using (var generateLightmapBatch = BatchGroup.New(frame, 0, ResetViewScale, ApplyViewScale)) {
                SetRenderTargetBatch.AddNew(generateLightmapBatch, 0, BackgroundLightmap);
                ClearBatch.AddNew(generateLightmapBatch, 1, Materials.Clear, clearColor: new Color(16, 16, 16, 255));
                BackgroundRenderer.RenderLighting(frame, generateLightmapBatch, 2);
                ForegroundRenderer.RenderLighting(frame, generateLightmapBatch, 3);
                SetRenderTargetBatch.AddNew(generateLightmapBatch, 4, null);
            }

            using (var generateLightmapBatch2 = BatchGroup.New(frame, 1, ResetViewScale, ApplyViewScale)) {
                SetRenderTargetBatch.AddNew(generateLightmapBatch2, 0, ForegroundLightmap);
                ClearBatch.AddNew(generateLightmapBatch2, 1, Materials.Clear, clearColor: new Color(127, 127, 127, 255));
                ForegroundRenderer.RenderLighting(frame, generateLightmapBatch2, 2);
                SetRenderTargetBatch.AddNew(generateLightmapBatch2, 3, null);
            }

            ClearBatch.AddNew(frame, 2, Materials.Clear, clearColor: Color.Black);

            using (var bb = BitmapBatch.New(frame, 3, BackgroundRenderer.WorldSpaceLightmappedBitmap)) {
                for (var i = 0; i < 2; i++) {
                    var layer = Layers[i];
                    var dc = new BitmapDrawCall(layer, Vector2.Zero);
                    dc.Textures.Texture2 = BackgroundLightmap;
                    dc.SortKey = i;
                    bb.Add(dc);
                }
            }

            using (var bb = BitmapBatch.New(frame, 4, BackgroundRenderer.WorldSpaceLightmappedBitmap)) {
                for (var i = 2; i < Layers.Length; i++) {
                    var layer = Layers[i];
                    var dc = new BitmapDrawCall(layer, Vector2.Zero);
                    dc.Textures.Texture2 = ForegroundLightmap;
                    dc.SortKey = i;
                    bb.Add(dc);
                }
            }

            if (ShowOutlines || (Dragging != null))
                BackgroundRenderer.RenderOutlines(frame, 5, true);
        }
    }
}
