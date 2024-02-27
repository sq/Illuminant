using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Illuminant.Util;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Imperative;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.RasterStroke;
using Squared.Util;

namespace TestGame.Scenes {
    // These aren't illuminant specific but who cares
    public class Strokes : Scene {
        Toggle BlendInLinearSpace, WorldSpace, Textured, PolygonGap, Ramp;

        RasterBrush Brush;

        [Group("Basic Settings")]
        Slider Size, Spacing, Seed, Gamma, ShapeFactor;
        [Items("LineSegment")]
        [Items("Rectangle")]
        [Items("Polygon")]
        Dropdown<string> Type;
        [Group("Tapering")]
        Slider TaperIn, TaperOut,
            StartOffset, EndOffset;
        [Group("Shadow")]
        Slider ShadowOpacity, ShadowOffset, ShadowExpansion, ShadowHardness;

        Texture2D NozzleAtlas, RampTexture;

        private List<RasterPolygonVertex> Polygon = new List<RasterPolygonVertex>();
        private RasterPolygonVertex[] PolygonArray;
        private Vector2 LastPathPoint;
        private double LastPathPointTime;
        private bool CreatingPath;
        private const float PathPointDistance = 24f, PathPointIntervalSeconds = 0.33f;

        public Strokes (TestGame game, int width, int height)
            : base(game, width, height) {
            Size.Min = 0.5f;
            Size.Max = 400f;
            Size.Speed = 5f;
            Size.Value = 180f;
            Spacing.Min = 0.025f;
            Spacing.Max = 3f;
            Spacing.Value = 0.2f;
            Spacing.Speed = 0.025f;
            TaperIn.Min = 0f;
            TaperIn.Max = 600f;
            TaperIn.Value = 100f;
            TaperIn.Speed = 5f;
            TaperOut.Min = 0f;
            TaperOut.Max = 600f;
            TaperOut.Value = 50f;
            TaperOut.Speed = 5f;
            StartOffset.Min = 0f;
            StartOffset.Max = 1f;
            StartOffset.Value = 0f;
            StartOffset.Speed = 0.05f;
            EndOffset.Min = 0f;
            EndOffset.Max = 1f;
            EndOffset.Value = 0f;
            EndOffset.Speed = 0.05f;
            Seed.Min = 0;
            Seed.Max = 512;
            Seed.Speed = 1;
            Seed.Integral = true;
            BlendInLinearSpace.Value = true;
            Textured.Value = false;
            Type.Value = "Polygon";
            ShadowOpacity.Min = 0;
            ShadowOpacity.Max = 1;
            ShadowOpacity.Speed = 0.05f;
            ShadowOpacity.Value = 0.5f;
            ShadowHardness.Min = 0;
            ShadowHardness.Max = 1;
            ShadowHardness.Speed = 0.05f;
            ShadowHardness.Value = 0.75f;
            ShadowExpansion.Min = -64f;
            ShadowExpansion.Max = 64f;
            ShadowExpansion.Speed = 0.25f;
            ShadowOffset.Min = -32f;
            ShadowOffset.Max = 32f;
            ShadowOffset.Value = 4f;
            ShadowOffset.Speed = 0.25f;
            Gamma.Min = 0.1f;
            Gamma.Max = 4f;
            Gamma.Value = 1f;
            Gamma.Speed = 0.1f;
            Gamma.Exponent = 4f;
            ShapeFactor.Min = 0f;
            ShapeFactor.Max = 1f;
            ShapeFactor.Value = 1f;
            ShapeFactor.Speed = 0.1f;
        }

        public override void LoadContent () {
            RampTexture = Game.TextureLoader.Load(
                "rainbow_ramp",
                new TextureLoadOptions { Premultiply = true, GenerateMips = true },
                cached: true
            );
            NozzleAtlas = Game.TextureLoader.Load(
                "isse-chain-link", 
                new TextureLoadOptions { Premultiply = true, GenerateMips = true }, 
                cached: true
            );
            Brush.NozzleCountX = Brush.NozzleCountY = 2;
            Brush.Scale = new BrushDynamics {
                Constant = 1,
                TaperFactor = 1,
            };
            /*
            FillPattern = Game.TextureLoader.Load(
                "stroke-fill",
                new TextureLoadOptions { Premultiply = true, GenerateMips = true },
                cached: true
            );
            */
        }

        public override void UnloadContent () {
        }

        public override void Draw (Frame frame) {
            var vt = ViewTransform.CreateOrthographic(Width, Height);
            vt.Position = new Vector2(64, 64);
            vt.Scale = Vector2.One * 1.2f;

            var now = (float)Time.Seconds;

            Brush.NozzleAtlas = Textured ? NozzleAtlas : null;
            Brush.Ramp = Ramp ? RampTexture : null;
            Brush.Spacing = Spacing.Value;
            Brush.SizePx = Size.Value;
            Brush.ShadowColor = new Color(96, 0, 24) * ShadowOpacity;
            Brush.ShadowOffset = new Vector2(ShadowOffset.Value * 0.1f, ShadowOffset.Value);
            Brush.ShadowExpansion = ShadowExpansion.Value;
            Brush.ShadowHardness = ShadowHardness.Value;
            Brush.Gamma = Gamma.Value;
            Brush.ShapeFactor = ShapeFactor.Value;

            using (var bg = BatchGroup.New(frame, 1, materialSet: Game.Materials)) {
                bg.ViewTransform = vt;
                var ir = new ImperativeRenderer(bg, Game.Materials, blendState: BlendState.NonPremultiplied);
                ir.RasterBlendInLinearSpace = BlendInLinearSpace.Value;
                ir.Clear(layer: 0, color: new Color(0, 96, 128));
                ir.WorldSpace = WorldSpace;

                var tl = new Vector2(80, 112);
                var br = new Vector2(1024, 800);
                var taper = new Vector4(TaperIn.Value, TaperOut.Value, StartOffset.Value, EndOffset.Value);

                switch (Type.Value) {
                    case "LineSegment":
                    case "Rectangle":
                        ir.RasterizeStroke(
                            (Type.Value == "LineSegment")
                                ? RasterStrokeType.LineSegment
                                : RasterStrokeType.Rectangle, 
                            tl, br, Color.White, Color.Black, Brush,
                            seed: Seed.Value,
                            taper: taper
                        );
                        break;
                    case "Polygon":
                        var verts = Polygon.Count > 1
                            ? new ArraySegment<RasterPolygonVertex>(PolygonArray)
                            : Shapes.GetPolygon(new Vector2(275, 325), 2.5f, PolygonGap, 0.25f);

                        if (verts.Count >= 2)
                            ir.RasterizeStroke(
                                verts,
                                Color.White, Ramp.Value ? Color.White : Color.Black, Brush,
                                seed: Seed.Value, taper: taper
                            );
                        ir.Layer += 1;

                        if (false)
                            foreach (var vert in verts)
                                ir.RasterizeEllipse(vert.Position, new Vector2(1.5f), Color.Yellow);

                        break;
                } 
            }
        }

        public override void UIScene (ref ContainerBuilder builder) {
            Brush.Scale = DynamicsEditor(ref builder, Brush.Scale, "Scale", 1f);
            Brush.AngleDegrees = DynamicsEditor(ref builder, Brush.AngleDegrees, "Angle", 360f);
            Brush.Flow = DynamicsEditor(ref builder, Brush.Flow, "Flow", 1f);
            Brush.BrushIndex = DynamicsEditor(ref builder, Brush.BrushIndex, "Brush Index", 4f);
            Brush.Hardness = DynamicsEditor(ref builder, Brush.Hardness, "Hardness", 1f);
            Brush.Color = DynamicsEditor(ref builder, Brush.Color, "Color", 1f);
        }

        private void DynamicsValue (
            ref ContainerBuilder builder, string tooltip, ref float value, 
            float minValue, float maxValue, bool newLine = false
        ) {
            var cflags = newLine
                ? ControlFlags.Layout_Fill_Row | ControlFlags.Layout_ForceBreak
                : ControlFlags.Layout_Fill_Row;
            var ctl = builder.New<ParameterEditor<float>>(cflags)
                .SetMinimumSize(0f, null)
                .SetRange<float>(minValue, maxValue)
                .SetTooltip(tooltip);
            ctl.Value(ref value);
            ctl.Control.Increment = (maxValue > 1) ? 1 : 0.05f;
        }

        private BrushDynamics DynamicsEditor (ref ContainerBuilder builder, BrushDynamics currentValue, string name, float maxValue) {
            var result = currentValue;
            var container = builder.TitledContainer(name, true);
            DynamicsValue(ref container, "Constant", ref result.Constant, 0, maxValue, true);
            DynamicsValue(ref container, "Increment", ref result.Increment, -maxValue, maxValue, false);
            DynamicsValue(ref container, "Taper", ref result.TaperFactor, -1f, 1f, true);
            DynamicsValue(ref container, "Noise", ref result.NoiseFactor, 0f, 1f, false);
            DynamicsValue(ref container, "Angle", ref result.AngleFactor, -1f, 1f, false);
            container.Finish();
            return result;
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                if ((Type.Value == "Polygon") && !Game.IsMouseOverUI) {
                    var pos = new Vector2(ms.X, ms.Y);
                    var shouldBeCreatingPath = ms.LeftButton == ButtonState.Pressed;
                    var distance = (pos - LastPathPoint).Length();

                    if (
                        (shouldBeCreatingPath != CreatingPath) || 
                        (CreatingPath && (distance > PathPointDistance)) ||
                        (CreatingPath && ((Time.Seconds - LastPathPointTime) > PathPointIntervalSeconds) && (distance > 6))
                    ) {
                        if (CreatingPath == false)
                            Polygon.Clear();

                        if ((Polygon.LastOrDefault().Position - pos).Length() > 1) {
                            Polygon.Add(pos);
                            // HACK
                            while (Polygon.Count > 1024)
                                Polygon.RemoveAt(0);
                            PolygonArray = Polygon.ToArray();
                            CreatingPath = shouldBeCreatingPath;
                            LastPathPoint = pos;
                            LastPathPointTime = Time.Seconds;
                        }
                    }
                }

                Game.IsMouseVisible = true;
            }
        }
    }
}
