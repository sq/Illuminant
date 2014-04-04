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
    public class RampTest : Scene {
        DefaultMaterialSet LightmapMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        bool ShowOutlines = true;

        LightObstructionLine Dragging = null;

        Texture2D TestImage, RampTexture;

        public RampTest (TestGame game, int width, int height)
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
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, multisampleCount, RenderTargetUsage.DiscardContents
                );
            }
        }

        public override void LoadContent () {
            LightmapMaterials = new DefaultMaterialSet(Game.Services);

            Environment = new LightingEnvironment();

            TestImage = Game.Content.Load<Texture2D>("ramp_test_image");

            RampTexture = Game.Content.Load<Texture2D>("LightGradients");

            Environment.LightSources.AddRange(new[] {
                new LightSource {
                    Position = new Vector2(128, 128),
                    Color = Vector4.One,
                    RampStart = 32,
                    RampEnd = 128,
                    RampMode = LightSourceRampMode.Linear
                },
                new LightSource {
                    Position = new Vector2(400, 128),
                    Color = Vector4.One,
                    RampStart = 32,
                    RampEnd = 128,
                    RampMode = LightSourceRampMode.Exponential
                },
                new LightSource {
                    Position = new Vector2(128, 400),
                    Color = Vector4.One,
                    RampStart = 32,
                    RampEnd = 128,
                    RampMode = LightSourceRampMode.Linear,
                    RampTexture = RampTexture
                },
                new LightSource {
                    Position = new Vector2(400, 400),
                    Color = Vector4.One,
                    RampStart = 32,
                    RampEnd = 128,
                    RampMode = LightSourceRampMode.Exponential,
                    RampTexture = RampTexture
                }
            });

            Renderer = new LightingRenderer(Game.Content, Game.RenderCoordinator, LightmapMaterials, Environment);
        }

        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, Width,
                Height, 0,
                0, 1
            );

            ClearBatch.AddNew(frame, 0, Game.ScreenMaterials.Clear, clearColor: Color.Black);

            Renderer.RenderLighting(frame, frame, 1);

            using (var bg = BatchGroup.New(frame, 2)) {
                var dc = new BitmapDrawCall(TestImage, new Vector2(0, 550), 0.55f);

                using (var bb = BitmapBatch.New(bg, 0, Renderer.Materials.ScreenSpaceBitmap))
                    bb.Add(ref dc);

                dc.Position.X += 600;
                dc.Textures = new TextureSet(dc.Texture, RampTexture);

                using (var bb2 = BitmapBatch.New(bg, 1, Renderer.IlluminantMaterials.ScreenSpaceRampBitmap, samplerState2: SamplerState.LinearClamp))
                    bb2.Add(ref dc);
            }

            if (ShowOutlines)
                Renderer.RenderOutlines(frame, 2, true);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                if (KeyWasPressed(Keys.O))
                    ShowOutlines = !ShowOutlines;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
            }
        }

        public override string Status {
            get {
                return "";
            }
        }
    }
}
