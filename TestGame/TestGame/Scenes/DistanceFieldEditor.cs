
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
    public class DistanceFieldEditor : Scene {
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        bool ShowOutlines      = false;
        bool ShowSurfaces      = true;
        bool ShowDistanceField = false;

        int  SelectedObject = 0;

        public DistanceFieldEditor (TestGame game, int width, int height)
            : base(game, 256, 256) {
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width, Height, true,
                    Width, Height, 128, true
                ) {
                    RenderScale = 1.0f,
                    DistanceFieldResolution = 0.5f,
                    DistanceFieldUpdateRate = 64
                }
            );

            Environment.GroundZ = 0;
            Environment.MaximumZ = 256;
            Environment.ZToYMultiplier = 2.5f;

            var offset = new Vector3(32, 32, 32);
            var size = new Vector3(24, 24, 24);

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Box,
                offset, size
            ));

            offset.X += (size.X * 2) + 8;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid,
                offset, size                
            ));

            offset.X += (size.X * 2) + 8;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Cylinder,
                offset, size                
            ));

            offset = new Vector3(90, 90, 90);

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid,
                offset, new Vector3(48, 24, 24)
            ));

            offset.X += 32;
            offset.Y += 12;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid,
                offset, new Vector3(32, 16, 16)
            ));

            offset.X += 24;
            offset.Y += 8;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid,
                offset, new Vector3(24, 8, 8)
            ));
        }

        private void Visualize (Frame frame, float x, float y, float size, Vector3 gaze) {
            var rect = Bounds.FromPositionAndSize(new Vector2(x, y), new Vector2(size, size));

            if (ShowSurfaces)
                Renderer.VisualizeDistanceField(
                    rect, gaze, frame, 2, VisualizationMode.Surfaces
                );

            if (ShowOutlines)
                Renderer.VisualizeDistanceField(
                    rect, gaze, frame, 3, VisualizationMode.Outlines
                );

            var ir = new ImperativeRenderer(frame, Game.Materials, 4, blendState: BlendState.AlphaBlend, autoIncrementLayer: true);
            var text = gaze.ToString();
            ir.DrawString(Game.Font, text, new Vector2(x + 1, y + 1), Color.Black, 0.7f);
            ir.DrawString(Game.Font, text, new Vector2(x, y), Color.White, 0.7f);
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            Renderer.UpdateFields(frame, -1);

            var visSize = Math.Min(
                Game.Graphics.PreferredBackBufferWidth / 3.1f,
                Game.Graphics.PreferredBackBufferHeight / 2.1f
            );

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, new Color(0, 32 / 255.0f, 32 / 255.0f, 1));

            const float pad = 8;

            Visualize(frame, 0, 0, visSize, -Vector3.UnitX);
            Visualize(frame, 0, visSize + pad, visSize, Vector3.UnitX);

            Visualize(frame, visSize + pad, 0, visSize, -Vector3.UnitY);
            Visualize(frame, visSize + pad, visSize + pad, visSize, Vector3.UnitY);

            Visualize(frame, (visSize + pad) * 2, 0, visSize, -Vector3.UnitZ);
            Visualize(frame, (visSize + pad) * 2, visSize + pad, visSize, Vector3.UnitZ);

            if (ShowDistanceField) {
                float dfScale = Math.Min(
                    (Game.Graphics.PreferredBackBufferWidth - 4) / (float)Renderer.DistanceField.Width,
                    (Game.Graphics.PreferredBackBufferHeight - 4) / (float)Renderer.DistanceField.Height
                );

                using (var bb = BitmapBatch.New(
                    frame, 998, Game.Materials.Get(
                        Game.Materials.ScreenSpaceBitmap,
                        blendState: BlendState.Opaque
                    ),
                    samplerState: SamplerState.PointClamp
                ))
                    bb.Add(new BitmapDrawCall(
                        Renderer.DistanceField, 
                        Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                        new Color(255, 255, 255, 255), dfScale
                    ));
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                SelectedObject = Arithmetic.Wrap((int)(ms.ScrollWheelValue / 160.0f), 0, Environment.Obstructions.Count - 1);

                if (KeyWasPressed(Keys.D1))
                    ShowSurfaces = !ShowSurfaces;

                if (KeyWasPressed(Keys.D2))
                    ShowOutlines = !ShowOutlines;

                if (KeyWasPressed(Keys.D3))
                    ShowDistanceField = !ShowDistanceField;

                var translation = Vector3.Zero;

                const float speed = 1;

                var ks = Game.KeyboardState;
                if (ks.IsKeyDown(Keys.W))
                    translation.Y -= speed;
                if (ks.IsKeyDown(Keys.S))
                    translation.Y += speed;
                if (ks.IsKeyDown(Keys.A))
                    translation.X -= speed;
                if (ks.IsKeyDown(Keys.D))
                    translation.X += speed;
                if (ks.IsKeyDown(Keys.F))
                    translation.Z -= speed;
                if (ks.IsKeyDown(Keys.R))
                    translation.Z += speed;

                if (translation.LengthSquared() > 0) {
                    Environment.Obstructions[SelectedObject].Center += translation;
                    Renderer.InvalidateFields();
                }
            }
        }

        public override string Status {
            get {
                return string.Format(
                    "sel #{0}", SelectedObject
                );
            }
        }
    }
}
