
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
                    Width, Height, 64, true
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
            var visSize = Math.Min(
                Game.Graphics.PreferredBackBufferWidth / 4.1f,
                Game.Graphics.PreferredBackBufferHeight / 2.1f
            );
            var visSize2 = new Vector2(visSize);
            const float pad = 8;

            var diagonal = new Vector3(0, -1, -1);
            diagonal.Normalize();

            Viewports = new[] {
                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2(0, 0), visSize2),
                    ViewAngle = -Vector3.UnitX
                },
                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2(0, visSize + pad), visSize2),
                    ViewAngle = Vector3.UnitX
                },

                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2(visSize + pad, 0), visSize2),
                    ViewAngle = -Vector3.UnitY
                },
                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2(visSize + pad, visSize + pad), visSize2),
                    ViewAngle = Vector3.UnitY
                },

                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 2, 0), visSize2),
                    ViewAngle = -Vector3.UnitZ
                },
                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 2, visSize + pad), visSize2),
                    ViewAngle = Vector3.UnitZ
                },

                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 3, 0), visSize2),
                    ViewAngle = diagonal
                }
            };
        }

        private void BuildObstructions () {
            var offset = new Vector3(32, 32, 32);
            var size = new Vector3(24, 24, 24);

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

            var extent = new Vector3(
                Renderer.Configuration.MaximumRenderSize.First,
                Renderer.Configuration.MaximumRenderSize.Second,
                Environment.MaximumZ
            );
            for (var z = 0; z <= 1; z++) {
                for (var y = 0; y <= 1; y++) {
                    for (var x = 0; x <= 1; x++) {
                        Environment.Obstructions.Add(new LightObstruction(
                            LightObstructionType.Ellipsoid,
                            extent * new Vector3(x, y, z),
                            Vector3.One * 10
                        ));
                    }
                }
            }
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

            if (false) {
                var obj = Environment.Obstructions[SelectedObject];
                Renderer.VisualizeDistanceField(
                    rect, gaze, frame, 4, obj, 
                    VisualizationMode.Outlines, BlendState.AlphaBlend,
                    color: new Vector4(0.15f, 0.4f, 0.0f, 0.15f)
                );
            }

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

        private Vector3 DeltaFromKeys (
            float speed,
            Keys minusX, Keys plusX,
            Keys minusY, Keys plusY,
            Keys minusZ, Keys plusZ
        ) {
            var ks = Game.KeyboardState;
            var result = Vector3.Zero;

            if (ks.IsKeyDown(minusX))
                result.X -= speed;
            if (ks.IsKeyDown(plusX))
                result.X += speed;
            if (ks.IsKeyDown(minusY))
                result.Y -= speed;
            if (ks.IsKeyDown(plusY))
                result.Y += speed;
            if (ks.IsKeyDown(minusZ))
                result.Z -= speed;
            if (ks.IsKeyDown(plusZ))
                result.Z += speed;

            return result;
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

                const float speed = 1;
                var translation = DeltaFromKeys(
                    speed,
                    Keys.A, Keys.D,
                    Keys.W, Keys.S,
                    Keys.F, Keys.R
                );
                var growth = DeltaFromKeys(
                    speed,
                    Keys.J, Keys.L,
                    Keys.I, Keys.K,
                    Keys.OemSemicolon, Keys.P
                );

                if ((translation.LengthSquared() > 0) || (growth.LengthSquared() > 0)) {
                    var obs = Environment.Obstructions[SelectedObject];
                    obs.Center += translation;
                    obs.Size = new Vector3(
                        Math.Max(1, obs.Size.X + growth.X),
                        Math.Max(1, obs.Size.Y + growth.Y),
                        Math.Max(1, obs.Size.Z + growth.Z)
                    );
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
