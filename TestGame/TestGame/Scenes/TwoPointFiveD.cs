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
    public class TwoPointFiveDTest : Scene {
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        Texture2D Background;
        float LightZ;

        const int ScaleFactor = 1;

        bool ShowGBuffer       = false;
        bool ShowLightmap      = true;
        bool ShowDistanceField = false;
        bool Timelapse         = false;
        bool GBuffer2p5        = false;
        bool TwoPointFiveD     = true;

        public TwoPointFiveDTest (TestGame game, int width, int height)
            : base(game, 1024, 1024) {
        }

        private void CreateRenderTargets () {
            int scaledWidth = (int)Width / ScaleFactor;
            int scaledHeight = (int)Height / ScaleFactor;

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
                    SurfaceFormat.Rgba64, DepthFormat.Depth24, multisampleCount, 
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
            Ellipse(center, 51f, 45f, 0, totalHeight * 128);
            Rect(center - baseSizeTL, center + baseSizeBR, 0.0f, baseHeight * 128);
            Rect(center - baseSizeTL, center + baseSizeBR, (totalHeight - capHeight) * 128, capHeight * 128);
        }

        public override void LoadContent () {
            Game.Materials = new DefaultMaterialSet(Game.Services);

            Environment = new LightingEnvironment();

            Background = Game.Content.Load<Texture2D>("sc3test");

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(1024, 1024) {
                    DistanceFieldResolution = 0.5f,
                    DistanceFieldSliceCount = 32,
                    DistanceFieldMinStepSize = 1.33f,
                    DistanceFieldMinStepSizeGrowthRate = 0.012f,
                    DistanceFieldLongStepFactor = 0.5f,
                    DistanceFieldOcclusionToOpacityPower = 0.5f,
                    DistanceFieldMaxConeRadius = 32,
                    DistanceFieldMaxStepCount = 96,
                    GBufferCaching = true,
                    DistanceFieldCaching = true
                }
            );

            var light = new LightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Radius = 24,
                RampLength = 550,
                RampMode = LightSourceRampMode.Exponential
            };

            Lights.Add(light);
            Environment.LightSources.Add(light);

            var light2 = new LightSource {
                Position = new Vector3(1024, 800, 320f),
                Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f),
                // FIXME: Implement directional lights and make this one
                Radius = 64,
                RampLength = 2048,
                RampMode = LightSourceRampMode.Linear
            };

            Lights.Add(light2);
            Environment.LightSources.Add(light2);

            var light3 = new LightSource {
                Position = new Vector3(500, 150, 220f),
                Color = new Vector4(0.6f, 0.4f, 0.2f, 0.33f),
                // FIXME: Implement directional lights and make this one
                Radius = 64,
                RampLength = 2048,
                RampMode = LightSourceRampMode.Linear
            };

            Lights.Add(light3);
            Environment.LightSources.Add(light3);

            Rect(new Vector2(330, 337), new Vector2(Width, 394), 0f, 55f);

            Pillar(new Vector2(97, 523));
            Pillar(new Vector2(719, 520));

            if (true)
                Environment.Obstructions.Add(new LightObstruction(
                    LightObstructionType.Box, 
                    new Vector3(500, 750, 0), new Vector3(50, 100, 20f)
                ));

            if (true)
                Environment.Obstructions.Add(new LightObstruction(
                    LightObstructionType.Ellipsoid, 
                    new Vector3(500, 750, 0), new Vector3(90, 45, 20f)
                ));

            if (false)
                Environment.HeightVolumes.Clear();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 128;
            Environment.ZToYMultiplier = 2.5f;
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            CreateRenderTargets();

            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;
            Renderer.Configuration.RenderTwoPointFiveDToGBuffer = GBuffer2p5;

            float time = (float)(Time.Seconds % 6);
            Renderer.Configuration.DistanceFieldMaxStepCount =
                Timelapse
                    ? (int)Arithmetic.Clamp(time * 24, 1, 128)
                    : 128;

            Renderer.UpdateFields(frame, -2);

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                (dm, _) => {
                    Game.Materials.PushViewTransform(ViewTransform.CreateOrthographic(Width, Height));
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

                if (ShowGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 4, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.GBuffer, Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, 1f / Renderer.Configuration.GBufferResolution
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

                if (KeyWasPressed(Keys.P)) {
                    GBuffer2p5 = !GBuffer2p5;
                    Renderer.InvalidateFields();
                }

                if (KeyWasPressed(Keys.D2)) {
                    TwoPointFiveD = !TwoPointFiveD;
                    Renderer.InvalidateFields();
                }

                if (KeyWasPressed(Keys.T))
                    Timelapse = !Timelapse;

                if (KeyWasPressed(Keys.D))
                    ShowDistanceField = !ShowDistanceField;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

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
