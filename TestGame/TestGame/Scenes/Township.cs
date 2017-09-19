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
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class Township : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        float LightZ;

        const int MultisampleCount = 0;
        const int LightmapScaleRatio = 1;
        const int MaxStepCount = 128;

        bool ShowGBuffer       = false;
        bool ShowLightmap      = false;
        bool ShowDistanceField = false;
        bool Deterministic     = false;
        float CameraX, CameraY;
        float CameraZoom = 1.0f;
        int CameraZoomIndex = 100;

        public Township (TestGame game, int width, int height)
            : base(game, 1024, 1024) {
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, MultisampleCount, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 64;

            DistanceField = new DistanceField(
                Game.RenderCoordinator, 4096, 4096, Environment.MaximumZ,
                4, 0.25f
            );

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024 / LightmapScaleRatio, 1024 / LightmapScaleRatio, true
                ) {
                    RenderScale = Vector2.One * (1.0f / LightmapScaleRatio),
                    DistanceFieldMinStepSize = 1f,
                    DistanceFieldLongStepFactor = 0.5f,
                    DistanceFieldOcclusionToOpacityPower = 0.7f,
                    DistanceFieldMaxConeRadius = 24,
                    DistanceFieldUpdateRate = 1,
                }
            ) {
                DistanceField = DistanceField
            };

            MovableLight = new SphereLightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Radius = 24,
                RampLength = 550,
                RampMode = LightSourceRampMode.Exponential
            };

            Environment.Lights.Add(MovableLight);

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(-0.75f, -0.7f, -0.33f),
                Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f)
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.35f, -0.05f, -0.75f),
                Color = new Vector4(0.5f, 0.3f, 0.15f, 0.3f)
            });

            {
                const int tileSize = 32;
                const int numTiles = 4096 / tileSize;

                var rng = new Random();
                for (var i = 0; i < 2048; i++) {
                    int x = rng.Next(0, numTiles), y = rng.Next(0, numTiles);
                    Environment.Obstructions.Add(new LightObstruction(
                        LightObstructionType.Box,
                        new Vector3(x * tileSize, y * tileSize, Environment.MaximumZ / 2),
                        new Vector3(tileSize, tileSize, Environment.MaximumZ)
                    ));
                }
            }

            /*
            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Box, 
                new Vector3(500, 750, 0), new Vector3(50, 100, 15f)
            ));
            */
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Renderer.UpdateFields(frame, -2);

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                (dm, _) => {
                    var vt = ViewTransform.CreateOrthographic(
                        Width, Height
                    );
                    vt.Position = new Vector2(CameraX, CameraY);
                    vt.Scale = new Vector2(CameraZoom);
                    Game.Materials.PushViewTransform(vt);
                },
                (dm, _) => {
                    Game.Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(bg, 0, Game.Materials.Clear, clearColor: Color.Black);

                Renderer.RenderLighting(bg, 1);
                Renderer.ResolveLighting(bg, 2, Width, Height);
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                if (ShowLightmap || true) {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                        samplerState: SamplerState.LinearClamp
                    ))
                        bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));
                } else {
                    /*
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(
                            ShowGBuffer
                                ? Game.Materials.ScreenSpaceBitmap
                                : Game.Materials.ScreenSpaceLightmappedBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    )) {
                        var dc = new BitmapDrawCall(
                            Background, Vector2.Zero, Color.White * (ShowGBuffer ? 0.7f : 1.0f)
                        );
                        dc.Textures = new TextureSet(dc.Textures.Texture1, Lightmap);
                        bb.Add(dc);
                    }
                    */
                }

                if (ShowDistanceField) {
                    float dfScale = Math.Min(
                        (Game.Graphics.PreferredBackBufferWidth - 4) / (float)Renderer.DistanceField.Texture.Width,
                        (Game.Graphics.PreferredBackBufferHeight - 4) / (float)Renderer.DistanceField.Texture.Height
                    );

                    using (var bb = BitmapBatch.New(
                        group, 3, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField.Texture, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            new Color(255, 255, 255, 255), dfScale
                        ));
                }

                if (ShowGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 4, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.GBuffer.Texture, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, LightmapScaleRatio
                        ));
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.L))
                    ShowLightmap = !ShowLightmap;

                if (KeyWasPressed(Keys.G))
                    ShowGBuffer = !ShowGBuffer;

                if (KeyWasPressed(Keys.D))
                    ShowDistanceField = !ShowDistanceField;

                if (KeyWasPressed(Keys.R))
                    Deterministic = !Deterministic;

                if (Game.KeyboardState.IsKeyDown(Keys.OemMinus))
                    CameraZoomIndex = Math.Min(300, CameraZoomIndex + 1);
                else if (Game.KeyboardState.IsKeyDown(Keys.OemPlus))
                    CameraZoomIndex = Math.Max(10, CameraZoomIndex - 1);

                CameraZoom = 100f / CameraZoomIndex;

                var scrollSpeed = 6 / CameraZoom;

                if (Game.KeyboardState.IsKeyDown(Keys.Right))
                    CameraX += scrollSpeed;
                else if (Game.KeyboardState.IsKeyDown(Keys.Left))
                    CameraX -= scrollSpeed;

                if (Game.KeyboardState.IsKeyDown(Keys.Up))
                    CameraY -= scrollSpeed;
                else if (Game.KeyboardState.IsKeyDown(Keys.Down))
                    CameraY += scrollSpeed;

                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                // FIXME: Zoom
                var mousePos = new Vector3((ms.X / CameraZoom) + CameraX, (ms.Y / CameraZoom) + CameraY, LightZ);

                if (Deterministic) {
                    MovableLight.Position = new Vector3(671, 394, 97.5f);
                    MovableLight.Radius = 24;
                } else {
                    MovableLight.Position = mousePos;
                    MovableLight.Radius = 24 / CameraZoom;
                }
            }
        }
    }
}
