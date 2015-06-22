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

namespace TestGame.Scenes {
    public class HeightVolumeTest : Scene {
        DefaultMaterialSet LightmapMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        bool ShowOutlines = true;
        bool ShowTerrainDepth = false;
        bool TwoPointFiveD = false;

        float LightZ = 0;

        public HeightVolumeTest (TestGame game, int width, int height)
            : base(game, width, height) {
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
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, multisampleCount, 
                    // YUCK
                    RenderTargetUsage.DiscardContents
                );
            }
        }

        public override void LoadContent () {
            LightmapMaterials = new DefaultMaterialSet(Game.Services);

            // Since the spiral is very detailed
            LightingEnvironment.DefaultSubdivision = 128f;

            Environment = new LightingEnvironment();

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, LightmapMaterials, Environment, 
                new RendererConfiguration(Width, Height)
            );

            var light = new LightSource {
                Position = new Vector2(64, 64),
                Color = new Vector4(1f, 1f, 1f, 1),
                Opacity = 0.9f,
                RampStart = 60,
                RampEnd = 600,
                RampMode = LightSourceRampMode.Exponential
            };

            Lights.Add(light);
            Environment.LightSources.Add(light);

            var rng = new Random(1234);
            for (var i = 0; i < 12; i++) {
                light = new LightSource {
                    Position = new Vector3(64, 64, rng.NextFloat(0.1f, 2.0f)),
                    Color = new Vector4((float)rng.NextDouble(0.1f, 1.0f), (float)rng.NextDouble(0.1f, 1.0f), (float)rng.NextDouble(0.1f, 1.0f), 1.0f),
                    RampStart = rng.NextFloat(32, 60),
                    RampEnd = rng.NextFloat(160, 250),
                    RampMode = LightSourceRampMode.Exponential
                };

                Lights.Add(light);
                Environment.LightSources.Add(light);
            }

            const float angleStep = (float)(Math.PI / 128);
            const int   heightTiers = 5;
            const float minHeight = 0f;
            const float maxHeight = 1f;

            Environment.GroundZ = 0;
            Environment.ZDistanceScale = 128;
            Environment.ZToYMultiplier = 200;

            var points = new List<Vector2>();

            for (float r = 0.9f, hs = (maxHeight - minHeight) / heightTiers, rs = -r / (heightTiers + 1), h = minHeight + hs; h <= maxHeight; h += hs, r += rs) {
                points.Clear();

                var rX = r * Width / 2f;
                var rY = r * Height / 2f;
                for (float a = 0, p2 = (float)(Math.PI * 2); a < p2; a += angleStep) {
                    points.Add(new Vector2(
                        ((float)Math.Cos(a) * rX) + (Width / 2f),
                        ((float)Math.Sin(a) * rY) + (Height / 2f)                    
                    ));
                }

                var volume = new SimpleHeightVolume(
                    new Polygon(points.ToArray()),
                    0.0f, h
                );

                Environment.HeightVolumes.Add(volume);
            }

            Environment.HeightVolumes.Add(new SimpleHeightVolume(
                Polygon.FromBounds(new Bounds(
                    new Vector2((Width * 0.5f) - 32f, 0f),
                    new Vector2((Width * 0.5f) + 32f, Height * 2)
                )),
                0f, 0.85f
            ));
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, Width,
                Height, 0,
                0, 1
            );

            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;

            CreateRenderTargets();

            Renderer.RenderHeightmap(frame, frame, -2);

            using (var bg = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap
            )) {
                ClearBatch.AddNew(bg, 0, LightmapMaterials.Clear, clearColor: Color.Black, clearZ: 0, clearStencil: 0);

                Renderer.RenderLighting(frame, bg, 1, intensityScale: 1);
            };

            ClearBatch.AddNew(frame, 0, Game.ScreenMaterials.Clear, clearColor: Color.Black);

            using (var bb = BitmapBatch.New(
                frame, 1,
                Game.ScreenMaterials.Get(Game.ScreenMaterials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                samplerState: SamplerState.PointClamp
            ))
                bb.Add(new BitmapDrawCall(
                    ShowTerrainDepth
                        ? Renderer.TerrainDepthmap
                        : Lightmap, Vector2.Zero
                ));

            if (ShowOutlines)
                Renderer.RenderOutlines(frame, 2, true);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.O))
                    ShowOutlines = !ShowOutlines;

                if (KeyWasPressed(Keys.T))
                    ShowTerrainDepth = !ShowTerrainDepth;

                if (KeyWasPressed(Keys.D2))
                    TwoPointFiveD = !TwoPointFiveD;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                // const float minZ = 0f, maxZ = 1.5f;
                // LightZ = Squared.Util.Arithmetic.PulseSine((float)gameTime.TotalGameTime.TotalSeconds * 0.66f, minZ, maxZ);
                LightZ = ms.ScrollWheelValue / 2048.0f;
                
                var mousePos = new Vector2(ms.X, ms.Y);

                var angle = gameTime.TotalGameTime.TotalSeconds * 0.125f;
                const float radius = 320f;

                var lightCenter = new Vector3(Width / 2, Height / 2, 0);

                Lights[0].Position = new Vector3(mousePos, LightZ);
                // Lights[0].RampEnd = 250f * (((1 - LightZ) * 0.25f) + 0.75f);


                float stepOffset = (float)((Math.PI * 2) / (Environment.LightSources.Count - 1));
                float offset = (float)(gameTime.TotalGameTime.TotalSeconds / 16 % 4);
                for (int i = 1; i < Environment.LightSources.Count; i++, offset += stepOffset) {
                    float localRadius = (float)(radius + (radius * Math.Sin(offset * 4f) * 0.5f));
                    float zFromRadius = 1.25f + (localRadius * -1f / Width);

                    Lights[i].Position = lightCenter + new Vector3(
                        (float)Math.Cos(angle + offset) * localRadius, 
                        (float)Math.Sin(angle + offset) * localRadius,
                        zFromRadius
                    );
                }
            }
        }

        public override string Status {
	        get { return String.Format("Light Z = {0:0.000}", LightZ); }
        }
    }
}
