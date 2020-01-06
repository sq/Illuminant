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
    public class ScrollingGeo : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        float LightZ;

        const int MultisampleCount = 0;
        const int LightmapScaleRatio = 1;
        const int MaxStepCount = 128;

        [Group("Visualization")]
        Toggle ShowGBuffer,
            ShowDistanceField;

        [Group("Camera")]
        Slider CameraDistance, CameraX, CameraY;


        [Group("Lighting")]
        Toggle Deterministic, DirectionalLight1, DirectionalLight2;
        [Group("Lighting")]
        Slider ZToYMultiplier, AmbientOcclusion;

        public ScrollingGeo (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            Deterministic.Value = true;
            DirectionalLight1.Value = false;
            DirectionalLight2.Value = true;
            AmbientOcclusion.Value = 0.5f;
            CameraDistance.Value = 80;

            ShowGBuffer.Key = Keys.G;
            ShowDistanceField.Key = Keys.D;
            Deterministic.Key = Keys.R;
            CameraDistance.MinusKey = Keys.OemMinus;
            CameraDistance.PlusKey = Keys.OemPlus;
            DirectionalLight1.Key = Keys.D1;
            DirectionalLight2.Key = Keys.D2;            

            CameraDistance.Min = 10;
            CameraDistance.Max = 300;
            CameraDistance.Speed = 10;

            CameraX.Max = CameraY.Max = 4096;
            CameraX.Speed = CameraY.Speed = 1;

            ZToYMultiplier.Min = 0.0f;
            ZToYMultiplier.Max = 1.0f;
            ZToYMultiplier.Speed = 0.05f;
            ZToYMultiplier.Value = 0.3f;

            AmbientOcclusion.Min = 0f;
            AmbientOcclusion.Max = 1f;
            AmbientOcclusion.Speed = 0.01f;
            AmbientOcclusion.Value = 0.5f;
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, MultisampleCount, 
                    RenderTargetUsage.PreserveContents
                );
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 256;

            DistanceField = new DistanceField(
                Game.RenderCoordinator, 4096, 4096, Environment.MaximumZ,
                16, 0.25f
            );

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024 / LightmapScaleRatio, 1024 / LightmapScaleRatio, true
                ) {
                    RenderScale = new Vector2(1.0f / LightmapScaleRatio),
                    MaximumFieldUpdatesPerFrame = 1,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = true,
                    RenderGroundPlane = true,
                    TwoPointFiveD = true
                }, Game.IlluminantMaterials
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
                Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f),
                Bounds = Bounds.FromPositionAndSize(new Vector2(128, 128), new Vector2(512, 1024))
            });

            Environment.Lights.Add(new DirectionalLightSource {
                Direction = new Vector3(0.1f, -0.05f, -1f),
                Color = new Vector4(0.6f, 0.0f, 0.0f, 0.3f)
            });

            {
                const int tileSize = 64;
                const int numTiles = 4096 / tileSize;

                var rng = new Random(123456);
                for (var i = 0; i < 900; i++) {
                    int x = rng.Next(0, numTiles), y = rng.Next(0, numTiles);
                    var obs = new LightObstruction(
                        LightObstructionType.Box,
                        new Vector3(x * tileSize, y * tileSize, 0),
                        new Vector3(20f, 20f, rng.Next(32, 200))
                    );
                    Environment.Obstructions.Add(obs);
                    var hvBounds = obs.Bounds3;
                    hvBounds = hvBounds.Expand(0.25f, 0.25f, 0);
                    var hv = new SimpleHeightVolume(Polygon.FromBounds(hvBounds.XY), 0, hvBounds.Size.Z + 0.5f);
                    hv.IsObstruction = false;
                    Environment.HeightVolumes.Add(hv);
                }
            }

            /*
            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Box, 
                new Vector3(500, 750, 0), new Vector3(50, 100, 15f)
            ));
            */
        }

        public override void UnloadContent () {
            DistanceField.Dispose();
            Renderer?.Dispose(); Renderer = null;
        }

        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            Environment.ZToYMultiplier = ZToYMultiplier.Value;

            var cz = new Vector2(100f / CameraDistance.Value);
            var cp = new Vector2(CameraX, CameraY);
            Vector2 cvp, uvo;
            Renderer.ComputeViewPositionAndUVOffset(
                cp,
                // FIXME: This is wrong because of the scaled resolve???????
                Lightmap.Width, Lightmap.Height,
                out cvp, out uvo
            );

            SetAO(Environment.Lights, AmbientOcclusion.Value, 4);

            Environment.Lights[1].Opacity = DirectionalLight1.Value ? 1 : 0;
            Environment.Lights[2].Opacity = DirectionalLight2.Value ? 1 : 0;

            // Renderer.InvalidateFields();

            Renderer.UpdateFields(frame, -2, viewportPosition: cvp, viewportScale: cz);

            var setLightingTransform = (Action<DeviceManager, object>)((dm, _) => {
                var vt = ViewTransform.CreateOrthographic(
                    Width, Height
                );
                vt.Position = cvp;
                vt.Scale = cz;
                Game.Materials.PushViewTransform(vt);
            });
            var popViewTransform = (Action<DeviceManager, object>)((dm, _) => {
                Game.Materials.PopViewTransform();
            });

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                setLightingTransform, popViewTransform                
            )) {
                ClearBatch.AddNew(bg, 0, Game.Materials.Clear, clearColor: Color.Black);

                var lighting = Renderer.RenderLighting(bg, 1);
                lighting.Resolve(bg, 2, Width, Height);
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                using (var bb = BitmapBatch.New(
                    group, 1,
                    Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                    samplerState: SamplerState.LinearClamp
                ))
                    bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero, new Bounds(uvo, Vector2.One + uvo)));

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
                        samplerState: SamplerState.LinearClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField.Texture.Get(), Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
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
                            Renderer.GBuffer.Texture.Get(), Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, LightmapScaleRatio
                        ));
                }

                if (false)
                using (var gbg = BatchGroup.New(group, 5, 
                    before: (dm, _) => {
                        var vt = Game.Materials.ViewTransform;
                        vt.Position = cp;
                        vt.Scale = cz;
                        Game.Materials.PushViewTransform(vt);
                    },
                    after: popViewTransform
                ))
                using (var gb = GeometryBatch.New(
                    gbg, 0,
                    Game.Materials.Get(Game.Materials.WorldSpaceGeometry, blendState: BlendState.Opaque)
                )) {
                    foreach (var o in Environment.Obstructions)
                        gb.AddFilledQuad(o.Bounds3.XY, Color.White);
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                var cameraZoom = 100f / CameraDistance.Value;

                var scrollSpeed = 3 / cameraZoom;

                if (Game.KeyboardState.IsKeyDown(Keys.Right))
                    CameraX.Value += scrollSpeed;
                else if (Game.KeyboardState.IsKeyDown(Keys.Left))
                    CameraX.Value -= scrollSpeed;

                if (Game.KeyboardState.IsKeyDown(Keys.Up))
                    CameraY.Value -= scrollSpeed;
                else if (Game.KeyboardState.IsKeyDown(Keys.Down))
                    CameraY.Value += scrollSpeed;

                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                // FIXME: Zoom
                var mousePos = new Vector3((ms.X / cameraZoom) + CameraX, (ms.Y / cameraZoom) + CameraY, LightZ);                

                if (Deterministic) {
                    MovableLight.Position = new Vector3(561, 354, 120.5f);
                    MovableLight.Radius = 36;
                } else {
                    MovableLight.Position = mousePos;
                    MovableLight.Radius = 24 / cameraZoom;
                }
            }
        }
    }
}
