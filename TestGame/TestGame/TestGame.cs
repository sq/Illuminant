using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        DefaultMaterialSet ScreenMaterials, LightmapMaterials;
        GaussianBlurMaterialSet BlurMaterials;
        LightmapMaterialSet ScreenLightmapRenderingMaterals, LightmapLightmapRenderingMaterials;

        DelegateMaterial AdditiveBitmapMaterial;
        DelegateMaterial MaskedForegroundMaterial;
        DelegateMaterial AOShadowMaterial;
        LightingEnvironment ForegroundEnvironment, BackgroundEnvironment;
        LightingRenderer ForegroundRenderer, BackgroundRenderer;

        Texture2D[] Layers = new Texture2D[4];
        Texture2D BricksLightMask;
        RenderTarget2D BackgroundLightmap, ForegroundLightmap;
        RenderTarget2D Background, Foreground;
        RenderTarget2D AOShadowScratch;

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

        bool ShowOutlines, ShowLightmap, ShowAOShadow = true, ShowBrickSpecular = true;

        const int LightmapMultisampleCount = 0;
        const float BaseLightmapScale = 1f;
        float LightmapScale;

        LightObstruction Dragging = null;

        KeyboardState PreviousKeyboardState;

        public TestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1257;
            Graphics.PreferredBackBufferHeight = 1250;
            Graphics.SynchronizeWithVerticalRetrace = false;
            Graphics.PreferMultiSampling = false;

            Content.RootDirectory = "Content";

            UseThreadedDraw = true;
            IsFixedTimeStep = false;

            PreviousKeyboardState = Keyboard.GetState();
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

        private IEnumerable<LightObstruction> MakeRoundedBox (Bounds bounds, float rounding) {
            var xo = new Vector2(rounding, 0);
            var yo = new Vector2(0, rounding);

            yield return new LightObstruction(
                bounds.TopLeft + xo, bounds.TopRight - xo
            );
            yield return new LightObstruction(
                bounds.TopRight - xo, bounds.TopRight + yo
            );
            yield return new LightObstruction(
                bounds.TopRight + yo, bounds.BottomRight - yo
            );
            yield return new LightObstruction(
                bounds.BottomRight - yo, bounds.BottomRight - xo
            );
            yield return new LightObstruction(
                bounds.BottomRight - xo, bounds.BottomLeft + xo
            );
            yield return new LightObstruction(
                bounds.BottomLeft + xo, bounds.BottomLeft - yo
            );
            yield return new LightObstruction(
                bounds.BottomLeft - yo, bounds.TopLeft + yo
            );
            yield return new LightObstruction(
                bounds.TopLeft + yo, bounds.TopLeft + xo
            );
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

                    BackgroundEnvironment.Obstructions.AddRange(
                        MakeRoundedBox(bounds, 8)
                    );
                }
            }
        }

        protected override void LoadContent () {
            base.LoadContent();

            ScreenMaterials = new DefaultMaterialSet(Content) {
                ViewportScale = new Vector2(1, 1),
                ViewportPosition = new Vector2(0, 0),
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0, GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, 0,
                    0, 1
                )
            };

            LightmapMaterials = new DefaultMaterialSet(Content);

            BlurMaterials = new GaussianBlurMaterialSet(LightmapMaterials, Content);

            ScreenLightmapRenderingMaterals = new LightmapMaterialSet(ScreenMaterials, Content);
            LightmapLightmapRenderingMaterials = new LightmapMaterialSet(LightmapMaterials, Content);

            LightingEnvironment.DefaultSubdivision = 512f;

            BackgroundEnvironment = new LightingEnvironment();
            ForegroundEnvironment = new LightingEnvironment();

            BackgroundRenderer = new LightingRenderer(Content, LightmapMaterials, BackgroundEnvironment);
            ForegroundRenderer = new LightingRenderer(Content, LightmapMaterials, ForegroundEnvironment);

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

            AdditiveBitmapMaterial = new DelegateMaterial(
                LightmapMaterials.ScreenSpaceBitmap,
                new Action<DeviceManager>[] {
                    (dm) => dm.Device.BlendState = BlendState.Additive
                },
                new Action<DeviceManager>[0]
            );

            MaskedForegroundMaterial = new DelegateMaterial(
                LightmapLightmapRenderingMaterials.ScreenSpaceLightmappedBitmap,
                new Action<DeviceManager>[] {
                    (dm) => dm.Device.BlendState = BlendState.Additive
                },
                new Action<DeviceManager>[0]
            );

            AOShadowMaterial = new DelegateMaterial(
                BlurMaterials.ScreenSpaceVerticalGaussianBlur5Tap,
                new Action<DeviceManager>[] {
                    (dm) => dm.Device.BlendState = BackgroundRenderer.SubtractiveBlend
                },
                new Action<DeviceManager>[0]
            );
        }

        protected override void Update (GameTime gameTime) {
            var ks = Keyboard.GetState();

            if (ks.IsKeyDown(Keys.O) && !PreviousKeyboardState.IsKeyDown(Keys.O))
                ShowOutlines = !ShowOutlines;
            if (ks.IsKeyDown(Keys.L) && !PreviousKeyboardState.IsKeyDown(Keys.L))
                ShowLightmap = !ShowLightmap;
            if (ks.IsKeyDown(Keys.A) && !PreviousKeyboardState.IsKeyDown(Keys.A))
                ShowAOShadow = !ShowAOShadow;
            if (ks.IsKeyDown(Keys.B) && !PreviousKeyboardState.IsKeyDown(Keys.B))
                ShowBrickSpecular = !ShowBrickSpecular;

            PreviousKeyboardState = ks;

            var ms = Mouse.GetState();
            IsMouseVisible = true;

            LightmapScale = BaseLightmapScale + (ms.ScrollWheelValue / 200f);
            if (LightmapScale < 1f)
                LightmapScale = 1f;

            var mousePos = new Vector2(ms.X, ms.Y);

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

            if (false)
            Console.WriteLine(
                "BeginDraw = {0:000.000}ms Draw = {1:000.000}ms EndDraw = {2:000.000}ms",
                PreviousFrameTiming.BeginDraw.TotalMilliseconds, PreviousFrameTiming.Draw.TotalMilliseconds, PreviousFrameTiming.EndDraw.TotalMilliseconds
            );

            base.Update(gameTime);
        }

        private void CreateRenderTargets () {
            int scaledWidth = (int)Math.Ceiling(Graphics.PreferredBackBufferWidth / LightmapScale);
            int scaledHeight = (int)Math.Ceiling(Graphics.PreferredBackBufferHeight / LightmapScale);

            if (scaledWidth < 4)
                scaledWidth = 4;
            if (scaledHeight < 4)
                scaledHeight = 4;

            if ((BackgroundLightmap == null) || (scaledWidth != BackgroundLightmap.Width) || (scaledHeight != BackgroundLightmap.Height)) {
                if (BackgroundLightmap != null)
                    BackgroundLightmap.Dispose();

                if (ForegroundLightmap != null)
                    ForegroundLightmap.Dispose();

                if (AOShadowScratch != null)
                    AOShadowScratch.Dispose();

                BackgroundLightmap = new RenderTarget2D(
                    GraphicsDevice, scaledWidth, scaledHeight, false,
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, LightmapMultisampleCount, RenderTargetUsage.DiscardContents
                );

                ForegroundLightmap = new RenderTarget2D(
                    GraphicsDevice, scaledWidth, scaledHeight, false,
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, LightmapMultisampleCount, RenderTargetUsage.DiscardContents
                );

                AOShadowScratch = new RenderTarget2D(
                    GraphicsDevice, scaledWidth, scaledHeight, true,
                    SurfaceFormat.Alpha8, DepthFormat.None, 0, RenderTargetUsage.DiscardContents
                );
            }

            if (Background == null)
                Background = new RenderTarget2D(
                    GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents
                );

            if (Foreground == null)
                Foreground = new RenderTarget2D(
                    GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, true,
                    SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents
                );
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            CreateRenderTargets();

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, BackgroundLightmap.Width,
                BackgroundLightmap.Height, 0,
                0, 1
            );

            using (var backgroundGroup = BatchGroup.ForRenderTarget(frame, 0, Background)) {
                ClearBatch.AddNew(backgroundGroup, 1, ScreenMaterials.Clear, clearColor: Color.Transparent);

                using (var bb = BitmapBatch.New(backgroundGroup, 2, ScreenMaterials.WorldSpaceBitmap)) {
                    for (var i = 0; i < 1; i++) {
                        var layer = Layers[i];
                        var dc = new BitmapDrawCall(layer, Vector2.Zero);
                        dc.SortKey = i;
                        bb.Add(dc);
                    }
                }
            }

            using (var foregroundGroup = BatchGroup.ForRenderTarget(frame, 1, Foreground)) {
                ClearBatch.AddNew(foregroundGroup, 1, ScreenMaterials.Clear, clearColor: Color.Transparent);

                using (var bb = BitmapBatch.New(foregroundGroup, 2, ScreenMaterials.WorldSpaceBitmap)) {
                    for (var i = 1; i < Layers.Length; i++) {
                        var layer = Layers[i];
                        var dc = new BitmapDrawCall(layer, Vector2.Zero);
                        dc.SortKey = i;
                        bb.Add(dc);
                    }
                }
            }

            if (ShowBrickSpecular)
            using (var bricksLightGroup = BatchGroup.ForRenderTarget(frame, 2, ForegroundLightmap)) {
                ClearBatch.AddNew(bricksLightGroup, 1, LightmapMaterials.Clear, clearColor: new Color(0, 0, 0, 255), clearZ: 0, clearStencil: 0);
                ForegroundRenderer.RenderLighting(frame, bricksLightGroup, 2);
            }

            if (ShowAOShadow)
            using (var aoShadowFirstPassGroup = BatchGroup.ForRenderTarget(frame, 3, AOShadowScratch)) {
                ClearBatch.AddNew(aoShadowFirstPassGroup, 1, LightmapMaterials.Clear, clearColor: Color.Transparent);

                using (var bb = BitmapBatch.New(aoShadowFirstPassGroup, 2, BlurMaterials.ScreenSpaceHorizontalGaussianBlur5Tap)) {
                    bb.Add(new BitmapDrawCall(Foreground, Vector2.Zero, 1f / LightmapScale));
                }
            }

            using (var backgroundLightGroup = BatchGroup.ForRenderTarget(frame, 4, BackgroundLightmap)) {
                ClearBatch.AddNew(backgroundLightGroup, 1, LightmapMaterials.Clear, clearColor: new Color(40, 40, 40, 255), clearZ: 0, clearStencil: 0);

                BackgroundRenderer.RenderLighting(frame, backgroundLightGroup, 2);

                if (ShowBrickSpecular) {
                    using (var foregroundLightBatch = BitmapBatch.New(backgroundLightGroup, 3, MaskedForegroundMaterial)) {
                        var dc = new BitmapDrawCall(
                            ForegroundLightmap, Vector2.Zero
                        );
                        dc.Textures.Texture2 = BricksLightMask;
                        foregroundLightBatch.Add(dc);
                    }
                } else {
                    ForegroundRenderer.RenderLighting(frame, backgroundLightGroup, 3);
                }

                if (ShowAOShadow)
                using (var aoShadowBatch = BitmapBatch.New(backgroundLightGroup, 4, AOShadowMaterial)) {
                    var dc = new BitmapDrawCall(
                        AOShadowScratch, new Vector2(0, 4)
                    );
                    dc.MultiplyColor = Color.Black;
                    dc.AddColor = Color.White;

                    aoShadowBatch.Add(dc);
                }
            }

            using (var foregroundLightGroup = BatchGroup.ForRenderTarget(frame, 5, ForegroundLightmap)) {
                ClearBatch.AddNew(foregroundLightGroup, 1, LightmapMaterials.Clear, clearColor: new Color(127, 127, 127, 255), clearZ: 0, clearStencil: 0);
                ForegroundRenderer.RenderLighting(frame, foregroundLightGroup, 2);
            }

            SetRenderTargetBatch.AddNew(frame, 49, null);
            ClearBatch.AddNew(frame, 50, ScreenMaterials.Clear, clearColor: Color.Black, clearZ: 0, clearStencil: 0);

            if (ShowLightmap) {
                using (var bb = BitmapBatch.New(frame, 55, ScreenMaterials.WorldSpaceBitmap)) {
                    var dc = new BitmapDrawCall(BackgroundLightmap, Vector2.Zero, LightmapScale);
                    bb.Add(dc);
                }
            } else {
                using (var bb = BitmapBatch.New(frame, 55, ScreenLightmapRenderingMaterals.WorldSpaceLightmappedBitmap)) {
                    var dc = new BitmapDrawCall(Background, Vector2.Zero);
                    dc.Textures.Texture2 = BackgroundLightmap;
                    dc.SortKey = 0;

                    bb.Add(dc);

                    dc.Textures.Texture1 = Foreground;
                    dc.Textures.Texture2 = ForegroundLightmap;
                    dc.SortKey = 1;

                    bb.Add(dc);
                }
            }

            if (ShowOutlines || (Dragging != null))
                BackgroundRenderer.RenderOutlines(frame, 59, true);
        }
    }
}
