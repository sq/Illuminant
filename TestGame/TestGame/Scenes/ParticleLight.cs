using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Illuminant;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class ParticleLight : Scene {
        DefaultMaterialSet LightmapMaterials;

        DelegateMaterial MaskedForegroundMaterial;
        LightingEnvironment ForegroundEnvironment, BackgroundEnvironment;
        LightingRenderer ForegroundRenderer, BackgroundRenderer;

        Texture2D[] Layers = new Texture2D[4];
        RenderTarget2D BackgroundLightmap, ForegroundLightmap;
        RenderTarget2D Background, Foreground;

        Random SparkRNG = new Random();

        ParticleRenderer ParticleRenderer;
        ParticleSystem<Spark> Sparks;
        ParticleLightManager<Spark> SparkLights;

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

        const int LightmapMultisampleCount = 0;
        const float BaseLightmapScale = 1f;
        float LightmapScale;

        LightObstructionLine Dragging = null;

        public ParticleLight (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        private void AddAmbientLight (float x, float y, Bounds? clipBounds = null) {
            var ambient = new LightSource {
                Mode = LightSourceMode.Max,
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

        private void CreateRenderTargets () {
            int scaledWidth = (int)Math.Ceiling(Width / LightmapScale);
            int scaledHeight = (int)Math.Ceiling(Height / LightmapScale);

            if (scaledWidth < 4)
                scaledWidth = 4;
            if (scaledHeight < 4)
                scaledHeight = 4;

            if ((BackgroundLightmap == null) || (scaledWidth != BackgroundLightmap.Width) || (scaledHeight != BackgroundLightmap.Height)) {
                if (BackgroundLightmap != null)
                    BackgroundLightmap.Dispose();

                if (ForegroundLightmap != null)
                    ForegroundLightmap.Dispose();

                BackgroundLightmap = new RenderTarget2D(
                    Game.GraphicsDevice, scaledWidth, scaledHeight, false,
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, LightmapMultisampleCount, RenderTargetUsage.DiscardContents
                );

                ForegroundLightmap = new RenderTarget2D(
                    Game.GraphicsDevice, scaledWidth, scaledHeight, false,
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, LightmapMultisampleCount, RenderTargetUsage.DiscardContents
                );
            }

            if (Background == null)
                Background = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents
                );

            if (Foreground == null)
                Foreground = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, true,
                    SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents
                );
        }

        public override void LoadContent () {
            LightmapMaterials = new DefaultMaterialSet(Game.Services);

            LightingEnvironment.DefaultSubdivision = 512f;

            BackgroundEnvironment = new LightingEnvironment();
            ForegroundEnvironment = new LightingEnvironment();

            BackgroundRenderer = new LightingRenderer(Game.Content, Game.RenderCoordinator, LightmapMaterials, BackgroundEnvironment, Width, Height);
            ForegroundRenderer = new LightingRenderer(Game.Content, Game.RenderCoordinator, LightmapMaterials, ForegroundEnvironment, Width, Height);

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

            GenerateObstructionsFromTiles();

            Layers[0] = Game.Content.Load<Texture2D>("layers_bg");
            Layers[1] = Game.Content.Load<Texture2D>("layers_fg");
            Layers[2] = Game.Content.Load<Texture2D>("layers_chars");
            Layers[3] = Game.Content.Load<Texture2D>("layers_torches");

            MaskedForegroundMaterial = LightmapMaterials.ScreenSpaceLightmappedBitmap.SetStates(blendState: BlendState.Additive);

            ParticleRenderer = new ParticleRenderer(LightmapMaterials) {
                Viewport = new Bounds(Vector2.Zero, new Vector2(Width, Height))
            };

            Spark.Texture = Game.Content.Load<Texture2D>("spark");

            ParticleRenderer.Systems = new[] {
                Sparks = new ParticleSystem<Spark>(
                    new DotNetTimeProvider(),
                    BackgroundEnvironment
                )
            };

            SparkLights = new ParticleLightManager<Spark>(
                Sparks, 
                new[] { BackgroundEnvironment, ForegroundEnvironment },
                UpdateSectorLight
            );
        }

        protected void UpdateSectorLight (ParticleSystem<Spark>.ParticleCollection particles, LightSource lightSource) {
            int particleCount = 0;

            int rAccumulator = 0, gAccumulator = 0, bAccumulator = 0, aAccumulator = 0;
            Vector2 positionAccumulator = Vector2.Zero;

            Spark particle;

            using (var e = particles.GetEnumerator())
            while (e.GetNext(out particle)) {
                particleCount += 1;

                positionAccumulator += particle.Position;
                var particleColor = particle.GetColor();

                rAccumulator += particleColor.R;
                gAccumulator += particleColor.G;
                bAccumulator += particleColor.B;
                aAccumulator += particleColor.A;
            }

            lightSource.Position = (positionAccumulator / particleCount);

            float fParticleCount255 = particleCount * 255.0f;
            // FIXME: Need to unpremultiply
            lightSource.Color = new Vector4(
                rAccumulator / fParticleCount255,
                gAccumulator / fParticleCount255,
                bAccumulator / fParticleCount255,
                aAccumulator / fParticleCount255
            );

            lightSource.Color.W *= MathHelper.Clamp(particleCount / 20f, 0.1f, 1.0f) * 0.55f;

            lightSource.RampStart = 24 + MathHelper.Clamp(particleCount * 0.25f, 0, 24);
            lightSource.RampEnd = lightSource.RampStart + 32 + MathHelper.Clamp(particleCount * 0.5f, 0, 48);

            lightSource.RampMode = LightSourceRampMode.Exponential;
        }

        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, BackgroundLightmap.Width,
                BackgroundLightmap.Height, 0,
                0, 1
            );

            using (var backgroundGroup = BatchGroup.ForRenderTarget(frame, 0, Background)) {
                ClearBatch.AddNew(backgroundGroup, 1, Game.ScreenMaterials.Clear, clearColor: Color.Transparent);

                using (var bb = BitmapBatch.New(backgroundGroup, 2, Game.ScreenMaterials.WorldSpaceBitmap)) {
                    for (var i = 0; i < 1; i++) {
                        var layer = Layers[i];
                        var dc = new BitmapDrawCall(layer, Vector2.Zero);
                        dc.SortKey = i;
                        bb.Add(dc);
                    }
                }
            }

            using (var foregroundGroup = BatchGroup.ForRenderTarget(frame, 1, Foreground)) {
                ClearBatch.AddNew(foregroundGroup, 1, Game.ScreenMaterials.Clear, clearColor: Color.Transparent);

                using (var bb = BitmapBatch.New(foregroundGroup, 2, Game.ScreenMaterials.WorldSpaceBitmap)) {
                    for (var i = 1; i < Layers.Length; i++) {
                        var layer = Layers[i];
                        var dc = new BitmapDrawCall(layer, Vector2.Zero);
                        dc.SortKey = i;
                        bb.Add(dc);
                    }
                }
            }

            BackgroundRenderer.RenderHeightmap(frame, frame, -16);
            ForegroundRenderer.RenderHeightmap(frame, frame, -16);

            using (var backgroundLightGroup = BatchGroup.ForRenderTarget(frame, 4, BackgroundLightmap)) {
                ClearBatch.AddNew(backgroundLightGroup, 1, LightmapMaterials.Clear, clearColor: new Color(32, 32, 32, 255), clearZ: 0, clearStencil: 0);
                BackgroundRenderer.RenderLighting(frame, backgroundLightGroup, 2);
            }

            using (var foregroundLightGroup = BatchGroup.ForRenderTarget(frame, 5, ForegroundLightmap)) {
                ClearBatch.AddNew(foregroundLightGroup, 1, LightmapMaterials.Clear, clearColor: new Color(96, 96, 96, 255), clearZ: 0, clearStencil: 0);
                ForegroundRenderer.RenderLighting(frame, foregroundLightGroup, 2);
            }

            SetRenderTargetBatch.AddNew(frame, 49, null);
            ClearBatch.AddNew(frame, 50, Game.ScreenMaterials.Clear, clearColor: Color.Black, clearZ: 0, clearStencil: 0);

            if (ShowLightmap) {
                using (var bb = BitmapBatch.New(frame, 55, Game.ScreenMaterials.WorldSpaceBitmap)) {
                    var dc = new BitmapDrawCall(BackgroundLightmap, Vector2.Zero, LightmapScale);
                    bb.Add(dc);
                }

                ParticleRenderer.Draw(frame, 56);
            } else {
                var dc = new BitmapDrawCall(Background, Vector2.Zero);

                var material = LightmapMaterials.Get(LightmapMaterials.WorldSpaceLightmappedBitmap, blendState: BlendState.AlphaBlend);

                using (var bb = BitmapBatch.New(frame, 55, material)) {
                    dc.Textures = new TextureSet(Background, BackgroundLightmap);
                    dc.SortKey = 0;

                    bb.Add(dc);
                }

                ParticleRenderer.Draw(frame, 56);

                using (var bb = BitmapBatch.New(frame, 57, material)) {
                    dc.Textures = new TextureSet(Foreground, ForegroundLightmap);
                    dc.SortKey = 1;

                    bb.Add(dc);
                }
            }

            if (ShowOutlines || (Dragging != null))
                BackgroundRenderer.RenderOutlines(frame, 59, true);
        }

        public override void Update (GameTime gameTime) {
            Sparks.CapacityLimit = 2048;
            Sparks.RemoveParticlesWhenCapacityReached = true;

            if (Game.IsActive) {
                if (KeyWasPressed(Keys.O))
                    ShowOutlines = !ShowOutlines;
                if (KeyWasPressed(Keys.L))
                    ShowLightmap = !ShowLightmap;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

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

                const int sparkSpawnCount = 24;
                var sparkSpawnPosition = mousePos;

                for (var i = 0; i < sparkSpawnCount; i++)
                    Sparks.Add(new Spark(Sparks, sparkSpawnPosition, 3));
            }

            Sparks.Update();
            SparkLights.Update();
        }

        public override string Status {
            get {
                return String.Format("Particles: {0:000000} ({1:0000} removed by limit)", Sparks.Count, Sparks.ParticlesRemovedByLimit);
            }
        }
    }
}
