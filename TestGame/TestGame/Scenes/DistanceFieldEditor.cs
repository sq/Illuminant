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
using Nuke = NuklearDotNet.Nuklear;

namespace TestGame.Scenes {
    public class DistanceFieldEditor : Scene {
        public struct Viewport {
            public Bounds  Rectangle;
            public Vector3 ViewAngle;

            public Vector3 Up, Right;
        }

        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        [Group("Visualization")]
        Toggle ShowSurfaces, ShowOutlines, ShowDistanceField;

        [Group("Edit")]
        Slider SelectedObject;

        int?       ActiveViewportIndex = null;

        Viewport[] Viewports;

        public DistanceFieldEditor (TestGame game, int width, int height)
            : base(game, 256, 256) {

            ShowSurfaces.Key = Keys.D1;
            ShowSurfaces.Value = true;

            ShowOutlines.Key = Keys.D2;
            ShowDistanceField.Key = Keys.D3;
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 256;
            Environment.ZToYMultiplier = 2.5f;

            DistanceField = new DistanceField(
                Game.RenderCoordinator, Width, Height, Environment.MaximumZ,
                64, 0.5f
            );

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width, Height, true, true
                ) {
                    RenderScale = Vector2.One,
                    MaximumFieldUpdatesPerFrame = 64
                }
            ) {
                DistanceField = DistanceField
            };

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
                    Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 2, 0), visSize2),
                    ViewAngle = -Vector3.UnitZ
                },
                new Viewport {
                    Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 2, visSize + pad), visSize2),
                    ViewAngle = Vector3.UnitZ
                },

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
                    Rectangle = Bounds.FromPositionAndSize(new Vector2((visSize + pad) * 3, 0), visSize2),
                    ViewAngle = diagonal
                },
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
                LightObstructionType.Box,
                offset, new Vector3(12, 12, 36)
            ));

            if (false) {
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

            SelectedObject.Max = Environment.Obstructions.Count - 1;
            SelectedObject.Speed = 1;
        }

        private string DirectionToText (Vector3 dir) {
            if (dir.X != 0)
                return (dir.X > 0) ? "+X" : "-X";
            if (dir.Y != 0)
                return (dir.Y > 0) ? "+Y" : "-Y";
            if (dir.Z != 0)
                return (dir.Z > 0) ? "+Z" : "-Z";

            return "?";
        }

        private void Visualize (Frame frame, ref Viewport viewport) {
            var visInfo = new VisualizationInfo();
            var rect = viewport.Rectangle;
            var gaze = viewport.ViewAngle;

            if (ShowSurfaces)
                visInfo = Renderer.VisualizeDistanceField(
                    rect, gaze, frame, 2, mode: VisualizationMode.Surfaces
                );

            if (ShowOutlines)
                visInfo = Renderer.VisualizeDistanceField(
                    rect, gaze, frame, 3, mode: VisualizationMode.Outlines
                );

            var obj = Environment.Obstructions[(int)SelectedObject.Value];
            Renderer.VisualizeDistanceField(
                rect, gaze, frame, 4, obj, 
                VisualizationMode.Outlines, BlendState.AlphaBlend,
                color: new Vector4(0.15f, 0.4f, 0.0f, 0.15f)
            );

            var ir = new ImperativeRenderer(frame, Game.Materials, blendState: BlendState.AlphaBlend, autoIncrementLayer: true);
            var text = gaze.ToString();
            ir.FillRectangle(rect, new Color(0, 32 / 255.0f, 16 / 255.0f, 1.0f), layer: 1);

            ir.DrawString(Game.Font, text, rect.TopLeft + Vector2.One, Color.Black, 0.7f, layer: 4);
            ir.DrawString(Game.Font, text, rect.TopLeft, Color.White, 0.7f, layer: 5);

            viewport.Up = visInfo.Up;
            viewport.Right = visInfo.Right;

            if (ShowSurfaces || ShowOutlines) {
                var rightText = DirectionToText(visInfo.Right);
                var pos = rect.BottomLeft - new Vector2(-2, Game.Font.LineSpacing * 0.6f);

                ir.DrawString(Game.Font, rightText, pos + Vector2.One, Color.Black, scale: 0.6f, layer: 4);
                ir.DrawString(Game.Font, rightText, pos, Color.White, scale: 0.6f, layer: 5);
            }
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            Renderer.UpdateFields(frame, -1);

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, new Color(0, 16 / 255.0f, 32 / 255.0f, 1));

            for (int i = 0; i < Viewports.Length; i++)
                Visualize(frame, ref Viewports[i]);

            if (ShowDistanceField) {
                float dfScale = Math.Min(
                    (Game.Graphics.PreferredBackBufferWidth - 4) / (float)Renderer.DistanceField.Texture.Width,
                    (Game.Graphics.PreferredBackBufferHeight - 4) / (float)Renderer.DistanceField.Texture.Height
                );

                using (var bb = BitmapBatch.New(
                    frame, 998, Game.Materials.Get(
                        Game.Materials.ScreenSpaceBitmap,
                        blendState: BlendState.Opaque
                    ),
                    samplerState: SamplerState.LinearClamp
                ))
                    bb.Add(new BitmapDrawCall(
                        Renderer.DistanceField.Texture, 
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

            // HACK: +y is down because *incoherent screaming*
            if (ks.IsKeyDown(minusY))
                result.Y += speed;
            if (ks.IsKeyDown(plusY))
                result.Y -= speed;

            if (ks.IsKeyDown(minusZ))
                result.Z -= speed;
            if (ks.IsKeyDown(plusZ))
                result.Z += speed;

            return result;
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                if ((ms.LeftButton == ButtonState.Released) && (ms.RightButton == ButtonState.Released)) {
                    ActiveViewportIndex = null;
                } else if (ActiveViewportIndex.HasValue) {
                    var viewport = Viewports[ActiveViewportIndex.Value];
                    var delta = new Vector2(ms.X - Game.PreviousMouseState.X, ms.Y - Game.PreviousMouseState.Y);
                    delta.X *= (Renderer.Configuration.MaximumRenderSize.First / viewport.Rectangle.Size.X);
                    delta.Y *= (Renderer.Configuration.MaximumRenderSize.Second / viewport.Rectangle.Size.Y);

                    var posChange = (viewport.Right * delta.X) + (viewport.Up * -delta.Y);

                    if (posChange.LengthSquared() > 0) {
                        var obs = Environment.Obstructions[(int)SelectedObject.Value];

                        if (Game.LeftMouse)
                            obs.Center += posChange;
                        if (Game.RightMouse) {
                            obs.Size += posChange;
                            if (obs.Size.X < 2)
                                obs.Size.X = 2;
                            if (obs.Size.Y < 2)
                                obs.Size.Y = 2;
                            if (obs.Size.Z < 2)
                                obs.Size.Z = 2;
                        }

                        Renderer.InvalidateFields();
                    }
                } else {
                    var mousePos = new Vector2(ms.X, ms.Y);

                    for (var i = 0; i < Viewports.Length; i++) {
                        if (Viewports[i].Rectangle.Contains(mousePos)) {
                            ActiveViewportIndex = i;
                            break;
                        }
                    }
                }

                // SelectedObject.Value = Arithmetic.Wrap((int)(ms.ScrollWheelValue / 160.0f), 0, Environment.Obstructions.Count - 1);

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
                    var obs = Environment.Obstructions[(int)SelectedObject.Value];
                    obs.Center += translation;
                    obs.Size = new Vector3(
                        Math.Max(2, obs.Size.X + growth.X),
                        Math.Max(2, obs.Size.Y + growth.Y),
                        Math.Max(2, obs.Size.Z + growth.Z)
                    );
                    Renderer.InvalidateFields();
                }

                /*
                float w = Game.Graphics.PreferredBackBufferWidth / 2f;
                float h = Game.Graphics.PreferredBackBufferHeight / 2f;
                var m2 = Matrix.CreateFromAxisAngle(Vector3.UnitX, -(float)(((ms.Y - h) / h) * Math.PI / 2));
                var magicAngle = Vector3.Transform(-Vector3.UnitY, m2);
                Viewports[Viewports.Length - 1].ViewAngle = magicAngle;
                */
            }
        }

        UTF8String sSelectedObject = new UTF8String("Selected Object");

        public unsafe override void UIScene () {
            var ctx = Game.Nuklear.Context;
            const float min = -128f;
            const float max = 256f + 128f;
            const float maxSize = 1024f;

            if (Nuke.nk_tree_push_hashed(ctx, NuklearDotNet.nk_tree_type.NK_TREE_TAB, sSelectedObject.pText, NuklearDotNet.nk_collapse_states.NK_MAXIMIZED, sSelectedObject.pText, sSelectedObject.Length, 64) != 0) {
                var obs = Environment.Obstructions[(int)SelectedObject.Value];

                var oldCenter = obs.Center;
                var oldSize = obs.Size;

                obs.Center.X = Nuke.nk_slide_float(ctx, min, obs.Center.X, max, 1f);
                obs.Center.Y = Nuke.nk_slide_float(ctx, min, obs.Center.Y, max, 1f);
                obs.Center.Z = Nuke.nk_slide_float(ctx, min, obs.Center.Z, max, 1f);

                obs.Size.X = Nuke.nk_slide_float(ctx, 1f, obs.Size.X, maxSize, 1f);
                obs.Size.Y = Nuke.nk_slide_float(ctx, 1f, obs.Size.Y, maxSize, 1f);
                obs.Size.Z = Nuke.nk_slide_float(ctx, 1f, obs.Size.Z, maxSize, 1f);

                if (
                    ((obs.Center - oldCenter).Length() >= 0.5f) ||
                    ((obs.Size - oldSize).Length() >= 0.5f)
                )
                    Renderer.InvalidateFields();

                Nuke.nk_tree_pop(ctx);
            }
        }
    }
}
