
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
        public struct Viewport {
            public Bounds  Rectangle;
            public Vector3 ViewAngle;
        }

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        bool ShowOutlines      = false;
        bool ShowSurfaces      = true;
        bool ShowDistanceField = false;

        int  SelectedObject = 0;

        Viewport[] Viewports;

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
                    DistanceFieldUpdateRate = 32
                }
            );

            Environment.GroundZ = 0;
            Environment.MaximumZ = 256;
            Environment.ZToYMultiplier = 2.5f;

            BuildViewports();
            BuildObstructions();
        }

        private void BuildViewports () {
            Viewports = new Viewport[6];

            var visSize = Math.Min(
                Game.Graphics.PreferredBackBufferWidth / 3.1f,
                Game.Graphics.PreferredBackBufferHeight / 2.1f
            );
            var visSize2 = new Vector2(visSize);
            const float pad = 8;

            Viewports[0] = new Viewport {
                Rectangle = Bounds.FromPositionAndSize(new Vector2(0, 0), visSize2),
                ViewAngle = -Vector3.UnitX
            };
            Viewports[1] = new Viewport {
                Rectangle = Bounds.FromPositionAndSize(new Vector2(0, visSize + pad), visSize2),
                ViewAngle = Vector3.UnitX
            };

            Viewports[2] = new Viewport {
                Rectangle = Bounds.FromPositionAndSize(new Vector2(visSize + pad, 0), visSize2),
                ViewAngle = -Vector3.UnitY
            };
            Viewports[3] = new Viewport {
                Rectangle = Bounds.FromPositionAndSize(new Vector2(visSize + pad, visSize + pad), visSize2),
                ViewAngle = Vector3.UnitY
            };

            Viewports[4] = new Viewport {
                Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 2, 0), visSize2),
                ViewAngle = -Vector3.UnitZ
            };
            Viewports[5] = new Viewport {
                Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 2, visSize + pad), visSize2),
                ViewAngle = Vector3.UnitZ
            };
        }

        private void BuildObstructions () {
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
                offset, new Vector3(12, 12, 36)
            ));
        }

        private void Visualize (Frame frame, Bounds rect, Vector3 gaze) {
            if (ShowSurfaces)
                Renderer.VisualizeDistanceField(
                    rect, gaze, frame, 2, mode: VisualizationMode.Surfaces
                );

            if (ShowOutlines)
                Renderer.VisualizeDistanceField(
                    rect, gaze, frame, 3, mode: VisualizationMode.Outlines
                );

            var obj = Environment.Obstructions[SelectedObject];
            Renderer.VisualizeDistanceField(
                rect, gaze, frame, 4, obj, 
                VisualizationMode.Outlines, BlendState.AlphaBlend,
                color: new Vector4(0.15f, 0.4f, 0.0f, 0.15f)
            );

            var ir = new ImperativeRenderer(frame, Game.Materials, 4, blendState: BlendState.AlphaBlend, autoIncrementLayer: true);
            var text = gaze.ToString();
            ir.DrawString(Game.Font, text, rect.TopLeft + Vector2.One, Color.Black, 0.7f);
            ir.DrawString(Game.Font, text, rect.TopLeft, Color.White, 0.7f);
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            Renderer.UpdateFields(frame, -1);

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, new Color(0, 32 / 255.0f, 32 / 255.0f, 1));

            foreach (var vp in Viewports)
                Visualize(frame, vp.Rectangle, vp.ViewAngle);

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
