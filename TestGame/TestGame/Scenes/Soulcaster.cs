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
    public class SoulcasterTest : Scene {
        DefaultMaterialSet LightmapMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();

        Texture2D Background;

        bool ShowOutlines = false;
        bool ShowTerrainDepth = false;

        public SoulcasterTest (TestGame game, int width, int height)
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
                    SurfaceFormat.Color, DepthFormat.Depth24Stencil8, multisampleCount, 
                    // YUCK
                    RenderTargetUsage.DiscardContents
                );
            }
        }

        HeightVolumeBase Rect (Vector2 a, Vector2 b, float hTL, float? hBR = null) {
            var result = new WallHeightVolume(
                Polygon.FromBounds(new Bounds(a, b)),
                hBR.GetValueOrDefault(hTL),
                hTL
            );
            Environment.HeightVolumes.Add(result);
            return result;
        }

        void Pillar (Vector2 tl, float bh) {
            Rect(new Vector2(-6, 193) + tl, new Vector2(-6 + 127, 346) + tl, bh);
            Rect(new Vector2(0, 128) + tl, new Vector2(118, 311) + tl, bh + 0.3f);
            Rect(new Vector2(0, 0) + tl, new Vector2(118, 128) + tl, bh + 0.5f);
        }

        public override void LoadContent () {
            LightmapMaterials = new DefaultMaterialSet(Game.Services);

            // Since the spiral is very detailed
            LightingEnvironment.DefaultSubdivision = 128f;

            Environment = new LightingEnvironment();

            Background = Game.Content.Load<Texture2D>("sc3test");

            Renderer = new LightingRenderer(Game.Content, Game.RenderCoordinator, LightmapMaterials, Environment, Width, Height);

            var light = new LightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 1),
                RampStart = 33,
                RampEnd = 350,
                RampMode = LightSourceRampMode.Exponential
            };

            Lights.Add(light);
            Environment.LightSources.Add(light);

            Rect(Vector2.Zero, new Vector2(Width, 610), 0.2f);
            Rect(new Vector2(0, 610), new Vector2(Width, 630), 0.2f, 0.0f).IsObstruction = false;

            Pillar(new Vector2(40, 233), 0.3f);
            Pillar(new Vector2(662, 231), 0.3f);
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            const float LightmapScale = 1f;

            LightmapMaterials.ViewportScale = new Vector2(1f / LightmapScale);
            LightmapMaterials.ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, Width,
                Height, 0,
                0, 1
            );

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
                    Background, Vector2.Zero
                ));

            using (var bb = BitmapBatch.New(
                frame, 2,
                Game.ScreenMaterials.Get(
                    ShowTerrainDepth
                        ? Game.ScreenMaterials.ScreenSpaceBitmap
                        : Game.ScreenMaterials.ScreenSpaceLightmappedBitmap,
                    blendState: BlendState.AlphaBlend
                ),
                samplerState: SamplerState.PointClamp
            )) {
                var dc = new BitmapDrawCall(
                    ShowTerrainDepth
                        ? Renderer.TerrainDepthmap
                        : Background,
                    Vector2.Zero, Color.White * (ShowTerrainDepth ? 0.9f : 1.0f)
                );
                dc.Textures = new TextureSet(dc.Textures.Texture1, Lightmap);
                bb.Add(dc);
            }

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

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;
                
                var mousePos = new Vector3(ms.X, ms.Y, Lights[0].Position.Z);

                var angle = gameTime.TotalGameTime.TotalSeconds * 0.125f;
                const float radius = 320f;

                Lights[0].Position = mousePos;
            }
        }

        public override string Status {
	        get { return ""; }
        }
    }
}
