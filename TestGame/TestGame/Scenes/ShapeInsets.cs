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
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Util;

namespace TestGame.Scenes {
    // These aren't illuminant specific but who cares
    public class ShapeInsets : Scene {
        Slider AnnularRadius;

        [Group("Orientation")]
        Slider Pitch, Yaw, Roll;

        [Group("Fill")]
        [Items("Natural")]
        [Items("Linear")]
        [Items("LinearEnclosed")]
        [Items("LinearEnclosing")]
        [Items("Radial")]
        [Items("RadialEnclosed")]
        [Items("RadialEnclosing")]
        [Items("Along")]
        [Items("Horizontal")]
        [Items("Vertical")]
        [Items("Angular")]
        [Items("Conical")]
        Dropdown<string> FillMode;

        [Items("Ellipse")]
        [Items("Rectangle")]
        [Items("Diamond")]
        Dropdown<string> CompositeType;

        [Items("Union")]
        [Items("Subtract")]
        [Items("Xor")]
        [Items("Intersection")]
        Dropdown<string> CompositeMode;

        [Group("Shadow")]
        Toggle ShadowInside;
        [Group("Shadow")]
        Slider ShadowSoftness, ShadowOffset, ShadowOpacity, ShadowExpansion;

        public ShapeInsets (TestGame game, int width, int height)
            : base(game, width, height) {
            Pitch.Min = -360f;
            Pitch.Max = 360f;
            Pitch.Value = 0f;
            Pitch.Speed = 5f;
            Yaw.Min = -360f;
            Yaw.Max = 360f;
            Yaw.Value = 0f;
            Yaw.Speed = 5f;
            Roll.Min = -360f;
            Roll.Max = 360f;
            Roll.Value = 0f;
            Roll.Speed = 5f;
            FillMode.Value = "Natural";
            AnnularRadius.Value = 0f;
            AnnularRadius.Min = 0f;
            AnnularRadius.Max = 32f;
            AnnularRadius.Speed = 0.25f;
            AnnularRadius.Exponent = 2;
            ShadowOffset.Min = -16f;
            ShadowOffset.Max = 16f;
            ShadowOffset.Value = 2f;
            ShadowOffset.Speed = 0.5f;
            ShadowOpacity.Min = 0f;
            ShadowOpacity.Max = 1f;
            ShadowOpacity.Value = 1f;
            ShadowOpacity.Speed = 0.1f;
            ShadowSoftness.Min = 0f;
            ShadowSoftness.Max = 32f;
            ShadowSoftness.Speed = 0.25f;
            ShadowSoftness.Value = 4f;
            ShadowSoftness.Exponent = 2;
            ShadowExpansion.Min = 0f;
            ShadowExpansion.Max = 8f;
            ShadowExpansion.Speed = 0.1f;
            ShadowExpansion.Exponent = 2;
        }

        public override void LoadContent () {
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var vt = Game.Materials.ViewTransform;
            vt.Position = new Vector2(0, 0);
            vt.Scale = Vector2.One;

            const float FillOpacity = 1.0f;
            const bool Hollow = false;
            const float OutlineSize = 2.0f;

            var batch = BatchGroup.New(
                frame, 0,
                materialSet: Game.Materials
            );
            batch.SetViewTransform(in vt);

            var fillMode = (RasterFillMode)Enum.Parse(typeof(RasterFillMode), FillMode.Value);

            Quaternion? orientation = Quaternion.CreateFromYawPitchRoll(
                MathHelper.ToRadians(Yaw), MathHelper.ToRadians(Pitch), MathHelper.ToRadians(Roll)
            );
            if (orientation.Value == Quaternion.Identity)
                orientation = null;

            var ir = new ImperativeRenderer(batch, Game.Materials, blendState: BlendState.NonPremultiplied);
            ir.Clear(layer: 0, color: new Color(0, 96, 128));

            var shadow = new RasterShadowSettings {
                Color = new pSRGBColor(0.4f, 0.02f, 0.22f, 1f) * ShadowOpacity,
                Softness = ShadowSoftness,
                Offset = new Vector2(ShadowOffset),
                Inside = ShadowInside,
                Expansion = ShadowExpansion,
            };

            var now = (float)Time.Seconds;
            var fs = new RasterFillSettings {
                Mode = fillMode,
            };

            var mousePos = new Vector2(Game.MouseState.X, Game.MouseState.Y);

            ir.ClearRasterComposites();
            ir.AddRasterComposite(new RasterShapeComposite {
                Type = (RasterShapeCompositeType)CompositeType.SelectedIndex,
                Mode = (RasterShapeCompositeMode)CompositeMode.SelectedIndex,
                Center = mousePos,
                Size = new Vector2(160, 128)
            });

            ir.RasterizeEllipse(
                Vector2.One * 600, new Vector2(420, 360), OutlineSize,
                innerColor: Hollow ? Color.Transparent : Color.White * FillOpacity,
                outerColor: Hollow ? Color.Transparent : Color.Black * FillOpacity,
                outlineColor: Color.White,
                layer: 1,
                fill: fs,
                annularRadius: AnnularRadius,
                shadow: shadow,
                orientation: orientation
            );

            ir.RasterizeLineSegment(
                new Vector2(32, 32), new Vector2(1024, 64), 1, 8, OutlineSize, 
                innerColor: Color.White * FillOpacity, 
                outerColor: Color.Black * FillOpacity, 
                outlineColor: Color.Red,
                fill: fs,
                annularRadius: AnnularRadius,
                shadow: shadow,
                layer: 1
            );

            float animatedRadius = 2f;

            var tl = new Vector2(80, 112);
            var br = new Vector2(512, 400);
            ir.RasterizeRectangle(
                tl, br, 
                radiusCW: new Vector4(animatedRadius + 4, animatedRadius, animatedRadius * 2, 0), 
                outlineRadius: OutlineSize, 
                innerColor: Hollow ? Color.Transparent : Color.Red * FillOpacity, 
                outerColor: Hollow ? Color.Transparent : new Color(0, 255, 0) * FillOpacity, 
                outlineColor: Color.Blue,
                fill: fs,
                annularRadius: AnnularRadius,
                layer: 1,
                shadow: shadow,
                orientation: orientation
            );

            ir.RasterizeRectangle(
                new Vector2(32, 256), new Vector2(32 + 6, 512), 4.5f, new Color(1f, 0, 0, 1), new Color(0.5f, 0, 0, 1),
                layer: 2, orientation: orientation
            );

            ir.RasterizeRectangle(
                new Vector2(48, 256), new Vector2(48 + 6, 512), 4.5f, new Color(1f, 1f, 0, 1), new Color(0.5f, 0.5f, 0, 1),
                layer: 2, orientation: orientation
            );

            ir.RasterizeRectangle(
                new Vector2(64, 256), new Vector2(64 + 6, 512), 4.5f, new Color(0f, 1f, 0, 1), new Color(0f, 0.5f, 0, 1),
                layer: 2, orientation: orientation
            );

            ir.RasterizeRectangle(
                new Vector2(80, 256), new Vector2(80 + 6, 512), 4.5f, new Color(0f, 1f, 1f, 1), new Color(0f, 0.5f, 0.5f, 1),
                layer: 2, orientation: orientation
            );

            ir.RasterizeRectangle(
                new Vector2(96, 256), new Vector2(96 + 6, 512), 4.5f, new Color(0f, 0f, 1f, 1), new Color(0f, 0f, 0.5f, 1),
                layer: 2, orientation: orientation
            );

            ir.RasterizeTriangle(
                new Vector2(640, 96), new Vector2(1200, 256), new Vector2(800, 512), 
                animatedRadius, OutlineSize,
                innerColor: Hollow ? Color.Transparent : Color.White * FillOpacity, 
                outerColor: Hollow ? Color.Transparent : Color.Black * FillOpacity, 
                outlineColor: Color.Blue,
                fill: fs,
                annularRadius: AnnularRadius,
                layer: 2,
                shadow: shadow,
                orientation: orientation
            );

            Vector2 a = new Vector2(1024, 64),
                b, c = new Vector2(1400, 256);
            b = mousePos;

            ir.RasterizeQuadraticBezier(
                a, b, c, 8, OutlineSize,
                innerColor: Color.White * FillOpacity, 
                outerColor: Color.Black * FillOpacity, 
                outlineColor: Color.Red,
                fill: fs,
                annularRadius: AnnularRadius,
                layer: 3,
                shadow: shadow
            );
        }

        public static ArraySegment<RasterPolygonVertex> GetPolygon (Vector2 position, float scale, bool polygonGap, float sizeBias) {
            RasterPolygonVertex v0 = new RasterPolygonVertex(new Vector2(32, 32), sizeBias),
                v1 = new Vector2(196, 64),
                v2 = new RasterPolygonVertex(new Vector2(228, 196), -sizeBias),
                v3 = new Vector2(32, 180),
                v3_5 = new RasterPolygonVertex(new Vector2(60, 0)) { Type = RasterVertexType.StartNew },
                v4 = new RasterPolygonVertex(new Vector2(90, -20)),
                v5 = new RasterPolygonVertex(new Vector2(32, -100), new Vector2(-100f));

            var verts = polygonGap
                ? new RasterPolygonVertex[] {
                    v0, v1, v2, v3, v0, v3_5, v4, v5
                }
                : new RasterPolygonVertex[] {
                    v0, v1, v2, v3, v4, v5
                };

            for (int i = 0; i < verts.Length; i++) {
                verts[i].Position = position + (verts[i].Position * scale);
                verts[i].ControlPoint = position + (verts[i].ControlPoint * scale);
            }

            return new ArraySegment<RasterPolygonVertex>(verts);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
