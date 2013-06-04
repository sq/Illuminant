using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
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
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame {
    public class TestGame : MultithreadedGame {
        GraphicsDeviceManager Graphics;
        DefaultMaterialSet ScreenMaterials, LightmapMaterials;

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
        
        Random SparkRNG = new Random();

        ParticleRenderer ParticleRenderer;
        ParticleSystem<Spark> Sparks;

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

        bool ShowOutlines, ShowLightmap, ShowAOShadow = false, ShowBrickSpecular = false;

        const int LightmapMultisampleCount = 0;
        const float BaseLightmapScale = 1f;
        float LightmapScale;

        readonly List<LightSource> Torches = new List<LightSource>();

        LightObstructionLine Dragging = null;

        KeyboardState PreviousKeyboardState;

        public TestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1257;
            Graphics.PreferredBackBufferHeight = 1250;
            Graphics.SynchronizeWithVerticalRetrace = true;
            // Graphics.SynchronizeWithVerticalRetrace = false;
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

            Torches.Add(torch);
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

        private LightObstructionLineStrip MakeRoundedBox (Bounds bounds, float rounding) {
            var xo = new Vector2(rounding, 0);
            var yo = new Vector2(0, rounding);

            return new LightObstructionLineStrip(
                bounds.TopLeft + xo, 
                bounds.TopRight - xo,
                bounds.TopRight + yo,
                bounds.BottomRight - yo,
                bounds.BottomRight - xo,
                bounds.BottomLeft + xo,
                bounds.BottomLeft - yo,
                bounds.TopLeft + yo,
                bounds.TopLeft + xo
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

                    BackgroundEnvironment.Obstructions.Add(
                        MakeRoundedBox(bounds, 8)
                    );
                }
            }
        }

        protected override void LoadContent () {
            base.LoadContent();

            ScreenMaterials = new DefaultMaterialSet(Services) {
                ViewportScale = new Vector2(1, 1),
                ViewportPosition = new Vector2(0, 0),
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0, GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, 0,
                    0, 1
                )
            };

            LightmapMaterials = new DefaultMaterialSet(Services);

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

            AdditiveBitmapMaterial = LightmapMaterials.ScreenSpaceBitmap.SetStates(blendState: BlendState.Additive);

            MaskedForegroundMaterial = LightmapMaterials.ScreenSpaceLightmappedBitmap.SetStates(blendState: BlendState.Additive);

            AOShadowMaterial = ScreenMaterials.ScreenSpaceVerticalGaussianBlur5Tap.SetStates(blendState: RenderStates.SubtractiveBlend);

            ParticleRenderer = new ParticleRenderer(LightmapMaterials) {
                Viewport = new Bounds(Vector2.Zero, new Vector2(Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight))
            };

            Spark.Texture = Content.Load<Texture2D>("spark");

            ParticleRenderer.Systems = new[] {
                Sparks = new ParticleSystem<Spark>(
                    new DotNetTimeProvider(),
                    new SparkUpdater(BackgroundEnvironment).Update,
                    Spark.Render,
                    Spark.GetPosition
                )
            };
        }

        protected override void Update (GameTime gameTime) {
            if (IsActive) {
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
                        BackgroundEnvironment.Obstructions.Add(Dragging = new LightObstructionLine(mousePos, mousePos));
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

            if (false)
            Console.WriteLine(
                "BeginDraw = {0:000.000}ms Draw = {1:000.000}ms EndDraw = {2:000.000}ms",
                PreviousFrameTiming.BeginDraw.TotalMilliseconds, PreviousFrameTiming.Draw.TotalMilliseconds, PreviousFrameTiming.EndDraw.TotalMilliseconds
            );

            const int sparkSpawnCount = 8;
            var sparkSpawnPosition = new Vector2(680, 320);

            for (var i = 0; i < sparkSpawnCount; i++)
                Sparks.Add(new Spark(Sparks, sparkSpawnPosition));

            Sparks.Update();

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

                using (var bb = BitmapBatch.New(aoShadowFirstPassGroup, 2, ScreenMaterials.ScreenSpaceHorizontalGaussianBlur5Tap)) {
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
                        dc.Textures = new TextureSet(dc.Textures.Texture1, BricksLightMask);
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
                using (var bb = BitmapBatch.New(frame, 55, LightmapMaterials.WorldSpaceLightmappedBitmap)) {
                    var dc = new BitmapDrawCall(Background, Vector2.Zero);
                    dc.Textures = new TextureSet(Background, BackgroundLightmap);
                    dc.SortKey = 0;

                    bb.Add(dc);

                    dc.Textures = new TextureSet(Foreground, ForegroundLightmap);
                    dc.SortKey = 1;

                    bb.Add(dc);
                }
            }

            if (ShowOutlines || (Dragging != null))
                BackgroundRenderer.RenderOutlines(frame, 59, true);

            ParticleRenderer.Draw(frame, 60);
        }
    }

    public struct Spark {
        public static readonly int DurationInFrames = (int)(60 * 3.25);
        public const float HalfPI = (float)(Math.PI / 2);
        public const float Gravity = 0.075f;
        public const float MaxFallRate = 4f;

        public static readonly Color HotColor = new Color(255, 225, 142);
        public static readonly Color ColdColor = new Color(63, 33, 13);

        public static Texture2D Texture;

        public int FramesLeft;
        public Vector2 Position, PreviousPosition;
        public Vector2 Velocity;

        public Spark (ParticleSystem<Spark> system, Vector2 position) {
            Position = PreviousPosition = position;
            FramesLeft = system.RNG.Next(DurationInFrames - 4, DurationInFrames + 4);
            Velocity = new Vector2(system.RNG.NextFloat(-2f, 2f), system.RNG.NextFloat(-3.5f, 1.5f));
        }

        public static Vector2 ApplyGravity (Vector2 velocity) {
            velocity.Y += Gravity;
            var length = velocity.Length();
            velocity /= length;
            return velocity * Math.Min(length, MaxFallRate);
        }

        public static void Render (ParticleSystem<Spark>.ParticleRenderArgs args) {
            Spark particle;

            float fDurationInFrames = DurationInFrames;

            while (args.Enumerator.GetNext(out particle)) {
                var delta = particle.Position - particle.PreviousPosition;
                var length = delta.Length();
                var angle = (float)(Math.Atan2(delta.Y, delta.X) - HalfPI);

                var lifeLeft = MathHelper.Clamp(particle.FramesLeft / fDurationInFrames, 0, 1);
                var lerpFactor = MathHelper.Clamp((1 - lifeLeft) * 1.4f, 0, 1);

                args.ImperativeRenderer.Draw(
                    Texture, particle.PreviousPosition, 
                    rotation: angle,
                    scale: new Vector2(0.25f, MathHelper.Clamp(length / 5f, 0.05f, 1.75f)),
                    multiplyColor: Color.Lerp(HotColor, ColdColor, lerpFactor) * lifeLeft,
                    blendState: BlendState.Additive
                );
            }
        }

        public static Vector2 GetPosition (ref Spark spark) {
            return spark.Position;
        }
    }

    public class SparkUpdater {
        public readonly LightingEnvironment LightingEnvironment;
        private readonly ListLineWriter LineWriter;

        public SparkUpdater (LightingEnvironment lightingEnvironment) {
            LightingEnvironment = lightingEnvironment;
            LineWriter = new ListLineWriter();
        }

        public void Update (ParticleSystem<Spark>.ParticleUpdateArgs args) {
            Spark particle;

            LineWriter.Reset();
            LightingEnvironment.EnumerateObstructionLinesInBounds(args.SectorBounds, LineWriter);

            var lines = LineWriter.Lines.GetBuffer();
            var lineCount = LineWriter.Lines.Count;

            while (args.Enumerator.GetNext(out particle)) {
                if (particle.FramesLeft <= 0) {
                    args.Enumerator.RemoveCurrent();
                    continue;
                }

                particle.FramesLeft -= 1;
                particle.PreviousPosition = particle.Position;
                particle.Position += particle.Velocity;

                float distance;
                bool intersected = false;

                for (var i = 0; i < lineCount; i++) {
                    var line = lines[i];

                    if (Geometry.DoLinesIntersect(particle.PreviousPosition, particle.Position, line.A, line.B, out distance)) {
                        var normal = line.B - line.A;
                        normal.Normalize();
                        normal = normal.Perpendicular();

                        // HACK: Fudge factor :(
                        var actualDistanceTravelled = (distance * 0.9f);
                        var intersection = particle.PreviousPosition + (particle.Velocity * actualDistanceTravelled);
                        particle.Position = intersection;

                        var oldVelocity = particle.Velocity;
                        Vector2.Reflect(ref oldVelocity, ref normal, out particle.Velocity);

                        intersected = true;
                        break;
                    }
                }

                if (!intersected)
                    particle.Velocity = Spark.ApplyGravity(particle.Velocity);

                args.ParticleMoved(ref particle, ref particle.PreviousPosition, ref particle.Position);
            }
        }
    }
}
