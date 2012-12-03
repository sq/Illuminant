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

        DelegateMaterial MaskedForegroundMaterial;
        LightingEnvironment ForegroundEnvironment, BackgroundEnvironment;
        LightingRenderer ForegroundRenderer, BackgroundRenderer;

        Texture2D[] Layers = new Texture2D[4];
        Texture2D BricksLightMask;
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
                Color = new Vector4(235 / 255.0f, 95 / 255.0f, 15 / 255f, 0.9f),
                RampStart = 80,
                RampEnd = 250
            };

            ForegroundEnvironment.LightSources.Add(torch);
        }

        private void AddAmbientLight (float x, float y, Bounds? clipBounds = null) {
            var ambient = new LightSource {
                Mode = LightSourceMode.Replace,
                Position = new Vector2(x, y),
                NeutralColor = new Vector4(32 / 255f, 32 / 255f, 32 / 255f, 1f),
                Color = new Vector4(1, 1, 1, 0.45f),
                RampStart = 2000,
                RampEnd = 2500,
                ClipRegion = clipBounds
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

            // Add a global sun
            AddAmbientLight(746, -300);

            // Add clipped suns for areas with weird shadowing behavior
            AddAmbientLight(746, 200, new Bounds(
                new Vector2(38, 33),
                new Vector2(678, 678)
            ));

            AddAmbientLight(740, 240, new Bounds(
                new Vector2(805, 34),
                new Vector2(1257, 546)
            ));

            AddAmbientLight(741, 750, new Bounds(
                new Vector2(0, 674),
                new Vector2(1257, 941)
            ));

            AddAmbientLight(741, 1025, new Bounds(
                new Vector2(0, 941),
                new Vector2(1257, 1250)
            ));

            AddTorch(102, 132);
            AddTorch(869, 132);
            AddTorch(102, 646);
            AddTorch(869, 645);

            GenerateObstructionsFromTiles();

            Layers[0] = Content.Load<Texture2D>("layers_bg");
            Layers[1] = Content.Load<Texture2D>("layers_fg");
            Layers[2] = Content.Load<Texture2D>("layers_chars");
            Layers[3] = Content.Load<Texture2D>("layers_torches");

            BricksLightMask = Content.Load<Texture2D>("layers_bricks_lightmask");

            MaskedForegroundMaterial = new DelegateMaterial(
                ForegroundRenderer.WorldSpaceLightmappedBitmap,
                new Action<DeviceManager>[] {
                    (dm) => dm.Device.BlendState = BlendState.Additive
                },
                new Action<DeviceManager>[0]
            );
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
            using (var bricksLightGroup = BatchGroup.New(frame, 0, ResetViewScale, ApplyViewScale)) {
                SetRenderTargetBatch.AddNew(bricksLightGroup, 0, ForegroundLightmap);
                ClearBatch.AddNew(bricksLightGroup, 1, Materials.Clear, clearColor: new Color(0, 0, 0, 255));
                ForegroundRenderer.RenderLighting(frame, bricksLightGroup, 2);
                SetRenderTargetBatch.AddNew(bricksLightGroup, 3, null);
            }

            using (var backgroundLightGroup = BatchGroup.New(frame, 1, ResetViewScale, ApplyViewScale)) {
                SetRenderTargetBatch.AddNew(backgroundLightGroup, 0, BackgroundLightmap);
                ClearBatch.AddNew(backgroundLightGroup, 1, Materials.Clear, clearColor: new Color(40, 40, 40, 255));

                BackgroundRenderer.RenderLighting(frame, backgroundLightGroup, 2);

                using (var foregroundLightBatch = BitmapBatch.New(backgroundLightGroup, 3, MaskedForegroundMaterial)) {
                    var dc = new BitmapDrawCall(
                        ForegroundLightmap, Vector2.Zero
                    );
                    dc.Textures.Texture2 = BricksLightMask;
                    foregroundLightBatch.Add(dc);
                }

                SetRenderTargetBatch.AddNew(backgroundLightGroup, 4, null);
            }

            using (var foregroundLightGroup = BatchGroup.New(frame, 2, ResetViewScale, ApplyViewScale)) {
                SetRenderTargetBatch.AddNew(foregroundLightGroup, 0, ForegroundLightmap);
                ClearBatch.AddNew(foregroundLightGroup, 1, Materials.Clear, clearColor: new Color(127, 127, 127, 255));
                ForegroundRenderer.RenderLighting(frame, foregroundLightGroup, 2);
                SetRenderTargetBatch.AddNew(foregroundLightGroup, 3, null);
            }

            ClearBatch.AddNew(frame, 3, Materials.Clear, clearColor: Color.Black);

            using (var bb = BitmapBatch.New(frame, 4, BackgroundRenderer.WorldSpaceLightmappedBitmap)) {
                for (var i = 0; i < 1; i++) {
                    var layer = Layers[i];
                    var dc = new BitmapDrawCall(layer, Vector2.Zero);
                    dc.Textures.Texture2 = BackgroundLightmap;
                    dc.SortKey = i;
                    bb.Add(dc);
                }
            }

            if (false)
                using (var bb = BitmapBatch.New(frame, 5, Materials.WorldSpaceBitmap))
                    bb.Add(new BitmapDrawCall(BackgroundLightmap, Vector2.Zero));

            using (var bb = BitmapBatch.New(frame, 5, BackgroundRenderer.WorldSpaceLightmappedBitmap)) {
                for (var i = 1; i < Layers.Length; i++) {
                    var layer = Layers[i];
                    var dc = new BitmapDrawCall(layer, Vector2.Zero);
                    dc.Textures.Texture2 = ForegroundLightmap;
                    dc.SortKey = i;
                    bb.Add(dc);
                }
            }

            using (var bb = BitmapBatch.New(frame, 6, Materials.WorldSpaceBitmap))
                bb.Add(new BitmapDrawCall(Layers[Layers.Length - 1], Vector2.Zero, Color.White * 0.1f));

            if (ShowOutlines || (Dragging != null))
                BackgroundRenderer.RenderOutlines(frame, 7, true);
        }
    }
}
