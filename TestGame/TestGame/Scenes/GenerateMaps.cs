﻿using System;
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
    public class GenerateMaps : Scene {
        RasterBrush Brush;

        [Items("Height")]
        [Items("Displacement")]
        [Items("Normals")]
        [Items("Displaced")]
        [Items("Lit")]
        Dropdown<string> Type;
        [Group("Brush Settings")]
        Slider Size, Spacing, Height1, Height2;
        [Group("Generator Settings")]
        Slider TapSpacing, MipBias, DisplacementScale;

        private List<RasterPolygonVertex> Polygon = new List<RasterPolygonVertex>();
        private RasterPolygonVertex[] PolygonArray;
        private Vector2 LastPathPoint;
        private double LastPathPointTime;
        private bool CreatingPath;
        private Texture2D Background;
        private AutoRenderTarget HeightMap, GeneratedMap;
        private IlluminantMaterials IlluminantMaterials;
        private const float PathPointDistance = 32f, PathPointIntervalSeconds = 0.33f;

        public GenerateMaps (TestGame game, int width, int height)
            : base(game, width, height) {
            Size.Min = 0.5f;
            Size.Max = 256f;
            Size.Speed = 5f;
            Size.Value = 64f;
            Spacing.Min = 0.025f;
            Spacing.Max = 3f;
            Spacing.Value = 0.2f;
            Spacing.Speed = 0.025f;
            Height1.Min = -2f;
            Height1.Max = 2f;
            Height1.Value = 0.25f;
            Height1.Speed = 0.1f;
            Height2.Min = -2f;
            Height2.Max = 2f;
            Height2.Value = 1f;
            Height2.Speed = 0.1f;
            Type.Value = "Height";
            TapSpacing.Min = 0.5f;
            TapSpacing.Max = 16f;
            TapSpacing.Speed = 1f;
            TapSpacing.Value = 1f;
            MipBias.Min = -1f;
            MipBias.Max = 8f;
            MipBias.Speed = 0.5f;
            MipBias.Value = 0f;
            DisplacementScale.Min = -16f;
            DisplacementScale.Max = 64f;
            DisplacementScale.Speed = 1f;
            DisplacementScale.Value = 4f;
        }

        public override void LoadContent () {
            Background = Game.TextureLoader.Load("vector-field-background");
            Brush.Scale = new BrushDynamics {
                Constant = 1,
                TaperFactor = 0.8f,
            };
            HeightMap = new AutoRenderTarget(Game.RenderCoordinator, Width, Height, true, SurfaceFormat.Single);
            GeneratedMap = new AutoRenderTarget(Game.RenderCoordinator, Width, Height, true, SurfaceFormat.Color);
            IlluminantMaterials = new IlluminantMaterials(Game.RenderCoordinator, Game.Materials);
        }

        public override void UnloadContent () {
        }

        public override void Draw (Frame frame) {
            var now = (float)Time.Seconds;

            Brush.Spacing = Spacing.Value;
            Brush.SizePx = Size.Value;

            var verts = Polygon.Count > 1
                ? new ArraySegment<RasterPolygonVertex>(PolygonArray)
                : Shapes.GetPolygon(new Vector2(275, 325), 2.5f, false, 0.25f);

            using (var bg = BatchGroup.ForRenderTarget(frame, -3, HeightMap)) {
                var ir = new ImperativeRenderer(bg, Game.Materials, blendState: BlendState.NonPremultiplied);
                ir.Clear(layer: 0, color: new Color(127, 127, 127));
                // We are feeding in linear values as-is and want it to blend them and then write them back out
                ir.RasterBlendInLinearSpace = false;
                ir.DisableDithering = true;

                var taper = new Vector4(64, 64, 0, 0);
                float h1 = (Height1.Value / 2f) + 0.5f, h2 = (Height2.Value / 2f) + 0.5f;
                pSRGBColor c1 = new pSRGBColor(new Vector4(h1, h1, h1, 1), true),
                    c2 = new pSRGBColor(new Vector4(h2, h2, h2, 1), true);

                if (verts.Count >= 2)
                    ir.RasterizeStroke(
                        verts,
                        c1, c2, Brush,
                        seed: 0f, taper: taper                  
                    );
            }

            using (var gm = BatchGroup.ForRenderTarget(frame, -2, GeneratedMap)) {
                var ir = new ImperativeRenderer(gm, Game.Materials, blendState: BlendState.NonPremultiplied);
                ir.Clear(layer: 0, color: new Color(127, 127, 127));
                ir.Parameters.Add("TapSpacingAndBias", new Vector3(1.0f / HeightMap.Width * TapSpacing.Value, 1.0f / HeightMap.Height * TapSpacing.Value, MipBias));
                ir.Parameters.Add("DisplacementScale", Vector2.One);

                IDynamicTexture tex1 = null;
                Material m = null;
                switch (Type.Value) {
                    case "Normals":
                    case "Lit":
                        tex1 = HeightMap;
                        m = IlluminantMaterials.HeightmapToNormals;
                        break;
                    case "Displacement":
                    case "Displaced":
                        tex1 = HeightMap;
                        m = IlluminantMaterials.HeightmapToDisplacement;
                        break;
                }

                if (tex1 != null)
                    ir.Draw(tex1, Vector2.Zero, material: m);
            }

            using (var fg = BatchGroup.New(frame, 1)) {
                var ir = new ImperativeRenderer(fg, Game.Materials, blendState: BlendState.NonPremultiplied);
                ir.Clear(layer: 0, color: new Color(0, 63, 127));
                ir.Parameters.Add("FieldIntensity", new Vector3(DisplacementScale.Value, DisplacementScale.Value, DisplacementScale.Value));

                Material m = null;
                AbstractTextureReference tex1 = default,
                    tex2 = default;
                switch (Type.Value) {
                    case "Height":
                        tex1 = new AbstractTextureReference(HeightMap);
                        break;
                    case "Normals":
                    case "Displacement":
                        tex1 = new AbstractTextureReference(GeneratedMap);
                        break;
                    case "Lit":
                    case "Displaced":
                    default:
                        tex2 = new AbstractTextureReference(Background);
                        tex1 = new AbstractTextureReference(GeneratedMap);
                        m = (Type.Value == "Displaced")
                            ? IlluminantMaterials.ScreenSpaceVectorWarp
                            : null;
                        break;
                }

                var ts = new TextureSet(tex1, tex2);
                var dc = new BitmapDrawCall(ts, Vector2.Zero, Bounds.Unit, Color.White, Vector2.One, Vector2.Zero, 0f);
                ir.Draw(dc, material: m);

                ir.Layer += 1;

                foreach (var vert in verts)
                    ir.RasterizeEllipse(vert.Position, new Vector2(1.5f), Color.Yellow);
            }
        }

        public override void UIScene (ref ContainerBuilder builder) {
            Brush.Scale = DynamicsEditor(ref builder, Brush.Scale, "Scale", 1f);
            Brush.Flow = DynamicsEditor(ref builder, Brush.Flow, "Flow", 1f);
            Brush.Hardness = DynamicsEditor(ref builder, Brush.Hardness, "Hardness", 1f);
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
                if (!Game.IsMouseOverUI) {
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
