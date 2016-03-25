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

namespace TestGame.Scenes {
    public class TwoPointFiveDTest : Scene {
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        Texture2D Background;
        float LightZ;

        bool ShowTerrainDepth  = false;
        bool ShowLightmap      = false;
        bool ShowDistanceField = false;

        public TwoPointFiveDTest (TestGame game, int width, int height)
            : base(game, 1024, 1024) {
        }

        private void CreateRenderTargets () {
            int scaledWidth = (int)Width;
            int scaledHeight = (int)Height;

            const int multisampleCount = 0;

            if (scaledWidth < 4)
                scaledWidth = 4;
            if (scaledHeight < 4)
                scaledHeight = 4;

            if ((Lightmap == null) || (scaledWidth != Lightmap.Width) || (scaledHeight != Lightmap.Height)) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, scaledWidth, scaledHeight, false,
                    SurfaceFormat.Color, DepthFormat.Depth24, multisampleCount, 
                    // YUCK
                    RenderTargetUsage.DiscardContents
                );
            }
        }

        HeightVolumeBase Rect (Vector2 a, Vector2 b, float z1, float height) {
            var result = new SimpleHeightVolume(
                Polygon.FromBounds(new Bounds(a, b)), z1, height 
            );
            Environment.HeightVolumes.Add(result);
            return result;
        }

        void Ellipse (Vector2 center, float radiusX, float radiusY, float z1, float height) {
            var numPoints = Math.Max(
                16,
                (int)Math.Ceiling((radiusX + radiusY) * 0.55f)
            );

            var pts = new Vector2[numPoints];
            float radiusStep = (float)((Math.PI * 2) / numPoints);
            float r = 0;

            for (var i = 0; i < numPoints; i++, r += radiusStep)
                pts[i] = new Vector2((float)Math.Cos(r) * radiusX, (float)Math.Sin(r) * radiusY) + center;
            
            var result = new SimpleHeightVolume(
                new Polygon(pts),
                z1, height
            );
            Environment.HeightVolumes.Add(result);
        }

        void Pillar (Vector2 center) {
            const float totalHeight = 0.69f;
            const float baseHeight  = 0.085f;
            const float capHeight   = 0.09f;

            var baseSizeTL = new Vector2(62, 65);
            var baseSizeBR = new Vector2(64, 57);
            Ellipse(center, 51f, 45f, 0, totalHeight);
            Rect(center - baseSizeTL, center + baseSizeBR, 0.0f, baseHeight);
            Rect(center - baseSizeTL, center + baseSizeBR, totalHeight - capHeight, capHeight);
        }

        public override void LoadContent () {
            Game.Materials = new DefaultMaterialSet(Game.Services);

            Environment = new LightingEnvironment();

            Background = Game.Content.Load<Texture2D>("sc3test");

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(1024, 1024) {
                    TwoPointFiveD = true,
                    DistanceFieldResolution = 0.5f,
                    DistanceFieldSliceCount = 48,
                    DistanceFieldStepSize = 1.33f,
                    DistanceFieldOcclusionToOpacityPower = 0.45f,
                    DistanceFieldConeGrowthRate = 0.2f,
                    DistanceFieldMaxConeRadius = 16,
                    DistanceFieldCaching = true
                }
            );

            var light = new LightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Radius = 32,
                RampLength = 400,
                RampMode = LightSourceRampMode.Exponential
            };

            Lights.Add(light);
            Environment.LightSources.Add(light);

            var light2 = new LightSource {
                Position = new Vector3(1024, 800, 2.5f),
                Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f),
                // FIXME: Implement directional lights and make this one
                Radius = 128,
                RampLength = 2048,
                RampMode = LightSourceRampMode.Linear
            };

            if (false) {
                Lights.Add(light2);
                Environment.LightSources.Add(light2);
            }

            Rect(new Vector2(330, 337), new Vector2(Width, 394), 0f, 0.435f);

            Pillar(new Vector2(97, 523));
            Pillar(new Vector2(719, 520));

            // Floating cylinders
            if (false) {
                Ellipse(new Vector2(420, 830), 40f, 40f, 0.33f, 0.20f);
                Ellipse(new Vector2(500, 825), 35f, 35f, 0.35f, 0.10f);
            }

            if (true)
                Environment.Obstructions.Add(new LightObstruction(
                    LightObstructionType.Box, 
                    new Vector3(500, 750, 0), new Vector3(50, 100, 0.1f)
                ));

            if (true)
                Environment.Obstructions.Add(new LightObstruction(
                    LightObstructionType.Ellipsoid, 
                    new Vector3(500, 750, 0), new Vector3(60, 30, 0.1f)
                ));

            if (true)
                Environment.HeightVolumes.Clear();

            Environment.ZDistanceScale = 128;
            Environment.ZToYMultiplier = 320;
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            CreateRenderTargets();

            Renderer.RenderHeightmap(frame, frame, -2);

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                (dm, _) => {
                    Game.Materials.PushViewTransform(ViewTransform.CreateOrthographic(Lightmap.Width, Lightmap.Height));
                },
                (dm, _) => {
                    Game.Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(bg, 0, Game.Materials.Clear, clearColor: Color.Black, clearZ: 0);

                Renderer.RenderLighting(frame, bg, 1, intensityScale: 1);
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                if (ShowLightmap) {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Lightmap, Vector2.Zero
                        ));
                } else {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(
                            ShowTerrainDepth
                                ? Game.Materials.ScreenSpaceBitmap
                                : Game.Materials.ScreenSpaceLightmappedBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    )) {
                        var dc = new BitmapDrawCall(
                            Background, Vector2.Zero, Color.White * (ShowTerrainDepth ? 0.7f : 1.0f)
                        );
                        dc.Textures = new TextureSet(dc.Textures.Texture1, Lightmap);
                        bb.Add(dc);
                    }
                }

                if (ShowDistanceField) {
                    float dfScale = Math.Min(
                        (Game.Graphics.PreferredBackBufferWidth - 4) / (float)Renderer.DistanceField.Width,
                        (Game.Graphics.PreferredBackBufferHeight - 4) / (float)Renderer.DistanceField.Height
                    );

                    using (var bb = BitmapBatch.New(
                        group, 3, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            new Color(255, 255, 0, 255), dfScale
                        ));
                }

                if (ShowTerrainDepth) {
                    using (var bb = BitmapBatch.New(
                        group, 4, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.TerrainDepthmap, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, 1f / Renderer.Configuration.HeightmapResolution
                        ));
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.L))
                    ShowLightmap = !ShowLightmap;

                if (KeyWasPressed(Keys.T))
                    ShowTerrainDepth = !ShowTerrainDepth;

                if (KeyWasPressed(Keys.D))
                    ShowDistanceField = !ShowDistanceField;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 2048.0f);

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                Lights[0].Position = mousePos;
            }
        }

        public override string Status {
            get { return String.Format("Light Z = {0:0.000}; Mouse Pos = {1},{2}", LightZ, Lights[0].Position.X, Lights[0].Position.Y); }
        }
    }
}
