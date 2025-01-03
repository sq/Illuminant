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
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Util;

namespace TestGame.Scenes {
    // These aren't illuminant specific but who cares
    public class Shapes : Scene {
        Toggle Animate, WorldSpace, ClosedPolygon, PolygonGap;
        Slider AnnularRadius;

        [Group("Star")]
        Slider StarPoints, StarTapering, StarThickness, StarTwirling;
        [Group("Arc")]
        Slider ArcStart, ArcLength, ArcSharpness;
        [Group("Orientation")]
        Slider Pitch, Yaw, Roll;

        [Items("Linear")]
        [Items("sRGB")]
        [Items("OkLab")]
        Dropdown<string> ColorSpace;

        [Group("Outline")]
        Toggle HardOutlines, InteriorGamma;
        [Group("Outline")]
        Slider Gamma, OutlineSize;

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

        [Group("Fill")]
        Toggle RepeatFill, UseRamp, Hollow;
            // DirectionalBevel;
        [Group("Fill")]
        Slider FillOffset, FillRangeStart, FillSize, FillAngle, FillPower, RampVOffset, FillOpacity,
            GradientCenterX, GradientCenterY, BevelRadius;
            // BevelDirection;

        [Group("Shadow")]
        Toggle ShadowInside;
        [Group("Shadow")]
        Slider ShadowSoftness, ShadowOffset, ShadowOpacity, ShadowExpansion;

        [Group("Texture")]
        Toggle UseTexture, CompositeTexture, PreserveAspectRatio, ScreenSpaceTexture, FinalTexture;
        [Group("Texture")]
        Slider TextureSize, TextureOrigin, TexturePosition, TextureSaturation, TextureBrightness;

        Texture2D Texture, RampTexture;

        AutoRenderTarget Scratch, RenderTo;

        public Shapes (TestGame game, int width, int height)
            : base(game, width, height) {
            Gamma.Min = 0.1f;
            Gamma.Max = 3.0f;
            Gamma.Value = 1.0f;
            Gamma.Speed = 0.1f;
            ColorSpace.Value = "Linear";
            OutlineSize.Min = 0f;
            OutlineSize.Max = 16f;
            OutlineSize.Value = 1f;
            OutlineSize.Speed = 0.5f;
            OutlineSize.Exponent = 2;
            ArcStart.Min = 0f;
            ArcStart.Max = 360f;
            ArcStart.Value = 0f;
            ArcStart.Speed = 5f;
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
            ArcLength.Min = 5f;
            ArcLength.Max = 360f;
            ArcLength.Value = 45f;
            ArcLength.Speed = 5f;
            ArcSharpness.Min = 0f;
            ArcSharpness.Max = 1.0f;
            HardOutlines.Value = true;
            FillMode.Value = "Natural";
            FillOffset.Min = -1f;
            FillOffset.Max = 1f;
            FillOffset.Speed = 0.1f;
            FillRangeStart.Value = 0f;
            FillRangeStart.Min = 0f;
            FillRangeStart.Max = 0.95f;
            FillRangeStart.Speed = 0.05f;
            FillSize.Value = 1f;
            FillSize.Min = 0.05f;
            FillSize.Max = 4f;
            FillSize.Speed = 0.05f;
            FillSize.Exponent = 4;
            FillPower.Value = 1f;
            FillPower.Min = 0.05f;
            FillPower.Max = 5f;
            FillPower.Speed = 0.05f;
            AnnularRadius.Value = 0f;
            AnnularRadius.Min = 0f;
            AnnularRadius.Max = 32f;
            AnnularRadius.Speed = 0.25f;
            AnnularRadius.Exponent = 2;
            FillAngle.Min = 0f;
            FillAngle.Max = 360f;
            FillAngle.Speed = 2f;
            FillOpacity.Min = 0f;
            FillOpacity.Max = 1f;
            FillOpacity.Speed = 0.05f;
            FillOpacity.Value = 1.0f;
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
            TextureSize.Value = 1;
            TextureSize.Max = 4;
            TextureSize.Min = 0.1f;
            TextureSize.Speed = 0.1f;
            TextureOrigin.Max = 1;
            TextureOrigin.Speed = 0.01f;
            TexturePosition.Min = -4;
            TexturePosition.Max = 4;
            TexturePosition.Speed = 0.01f;
            RampVOffset.Min = -1;
            RampVOffset.Max = 2;
            RampVOffset.Speed = 0.1f;
            TextureSaturation.Value = 1f;
            TextureSaturation.Min = 0f;
            TextureSaturation.Max = 2f;
            TextureSaturation.Speed = 0.05f;
            TextureBrightness.Value = 1f;
            TextureBrightness.Min = 0f;
            TextureBrightness.Max = 2f;
            TextureBrightness.Speed = 0.05f;
            StarPoints.Min = 3;
            StarPoints.Max = 32;
            StarPoints.Integral = true;
            StarPoints.Value = 5;
            StarTapering.Min = -16;
            StarTapering.Max = 16;
            StarThickness.Min = 0f;
            StarThickness.Max = 1f;
            StarThickness.Value = 0.5f;
            StarTwirling.Min = (float)(-Math.PI * 2f);
            StarTwirling.Max = (float)(Math.PI * 2f);
            GradientCenterX.Min = GradientCenterY.Min = 0f;
            GradientCenterX.Max = GradientCenterY.Max = 1f;
            GradientCenterX.Value = GradientCenterY.Value = 0.5f;
            BevelRadius.Min = -64f;
            BevelRadius.Max = 64f;
            // BevelDirection.Min = 0f;
            // BevelDirection.Max = 360f;
        }

        public override void LoadContent () {
            Texture = Game.TextureLoader.Load(
                "shape-texture", 
                new TextureLoadOptions { Premultiply = true, GenerateMips = true }, 
                cached: true
            );
            // RampTexture = Game.TextureLoader.Load("custom-gradient");
            RampTexture = Game.RampTexture;
            if (Scratch == null)
                Scratch = new AutoRenderTarget(Game.RenderCoordinator, 110, 110);
            if (RenderTo == null)
                RenderTo = new AutoRenderTarget(Game.RenderCoordinator, Width, Height, preferredFormat: SurfaceFormat.Color);
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var vt = ViewTransform.CreateOrthographic(RenderTo.Width, RenderTo.Height);
            vt.Position = new Vector2(64, 64);
            vt.Scale = Vector2.One * 1.2f;

            var batch = BatchGroup.ForRenderTarget(
                frame, -9900, RenderTo,
                materialSet: Game.Materials,
                viewTransform: vt
            );

            var gradientCenter = new Vector2(GradientCenterX, GradientCenterY);
            var fillMode = (RasterFillMode)Enum.Parse(typeof(RasterFillMode), FillMode.Value);

            Quaternion? orientation = Quaternion.CreateFromYawPitchRoll(
                MathHelper.ToRadians(Yaw), MathHelper.ToRadians(Pitch), MathHelper.ToRadians(Roll)
            );
            if (orientation.Value == Quaternion.Identity)
                orientation = null;

            var ir = new ImperativeRenderer(batch, Game.Materials, blendState: BlendState.NonPremultiplied);
            ir.Clear(layer: 0, color: new Color(0, 96, 128));
            if (InteriorGamma)
                ir.RasterGamma = Gamma.Value;
            else
                ir.RasterOutlineGamma = Gamma.Value;

            switch (ColorSpace.Value) {
                default:
                    ir.RasterBlendInLinearSpace = true;
                    break;
                case "OkLab":
                    ir.RasterBlendInOkLabSpace = true;
                    break;
                case "sRGB":
                    ir.RasterBlendInOkLabSpace = false;
                    ir.RasterBlendInLinearSpace = false;
                    break;
            }
            ir.RasterSoftOutlines = !HardOutlines.Value;
            ir.WorldSpace = WorldSpace;

            var shadow = new RasterShadowSettings {
                Color = new pSRGBColor(0.4f, 0.02f, 0.22f, 1f) * ShadowOpacity,
                Softness = ShadowSoftness,
                Offset = new Vector2(ShadowOffset),
                Inside = ShadowInside,
                Expansion = ShadowExpansion,
            };

            var textureSettings = new RasterTextureSettings {
                Mode = (CompositeTexture ? RasterTextureCompositeMode.Over : RasterTextureCompositeMode.Multiply) |
                    (ScreenSpaceTexture ? RasterTextureCompositeMode.ScreenSpaceLocal : default(RasterTextureCompositeMode)) |
                    (FinalTexture ? RasterTextureCompositeMode.AfterOutline : default(RasterTextureCompositeMode)),
                PreserveAspectRatio = PreserveAspectRatio,
                Origin = new Vector2(TextureOrigin.Value),
                Scale = new Vector2(TextureSize.Value),
                Position = new Vector2(TexturePosition.Value),
                Brightness = TextureBrightness.Value,
                Saturation = TextureSaturation.Value
            };

            var now = (float)Time.Seconds;
            var fs = new RasterFillSettings {
                Mode = fillMode,
                Offset = FillOffset,
                FillRange = new Vector2(FillRangeStart.Value, FillRangeStart.Value + FillSize.Value),
                Repeat = RepeatFill.Value,
                Angle = FillAngle,
                GradientPower = FillPower.Value,
                GradientCenter = gradientCenter,
                BevelRadius = BevelRadius,
                // BevelDirection = DirectionalBevel ? BevelDirection : (float?)null,
            };

            ir.RasterizeEllipse(
                Vector2.One * 600, new Vector2(420, 360), OutlineSize,
                innerColor: Hollow ? Color.Transparent : Color.White * FillOpacity,
                outerColor: Hollow ? Color.Transparent : Color.Black * FillOpacity,
                outlineColor: Color.White,
                layer: 1,
                fill: fs,
                annularRadius: AnnularRadius,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                shadow: shadow,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset),
                orientation: orientation
            );

            ir.RasterizeLineSegment(
                new Vector2(32, 32), new Vector2(1024, 64), 1, 8, OutlineSize, 
                innerColor: Color.White * FillOpacity, 
                outerColor: Color.Black * FillOpacity, 
                outlineColor: Color.Red,
                fill: fs,
                annularRadius: AnnularRadius,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset),
                shadow: shadow,
                layer: 1
            );

            float animatedRadius = (Animate.Value
                    ? Arithmetic.PulseSine(now / 3f, 2, 32)
                    : 2f);

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
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset),
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
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset),
                orientation: orientation
            );

            ir.RasterizeEllipse(new Vector2(200, 860), Vector2.One * 3, Color.Yellow, layer: 4);

            ir.RasterizeStar(
                new Vector2(200, 600),
                80f, (int)StarPoints.Value, 
                (float)Arithmetic.Lerp(2, StarPoints.Value, Animate ? (Time.Seconds % 4) / 4f : StarThickness),
                OutlineSize,
                innerColor: Color.White * FillOpacity, 
                outerColor: Color.Black * FillOpacity, 
                outlineColor: Color.Blue,
                fill: fs,
                annularRadius: AnnularRadius,
                layer: 2,
                shadow: shadow,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset),
                orientation: orientation,
                tapering: StarTapering.Value,
                twirling: StarTwirling.Value
            );

            ir.RasterizeArc(
                new Vector2(200, 860),
                Animate ? (float)(Time.Seconds) * 60f : ArcStart, ArcLength,
                120, 8, OutlineSize,
                innerColor: Color.White * FillOpacity, 
                outerColor: Color.Black * FillOpacity, 
                outlineColor: Color.Blue,
                fill: fs,
                annularRadius: AnnularRadius,
                layer: 2,
                shadow: shadow,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset),
                endRounding: 1.0f - ArcSharpness
            );

            Vector2 a = new Vector2(1024, 64),
                b, c = new Vector2(1400, 256);
            if (Animate) {
                float t = now / 2;
                float r = 160;
                b = new Vector2(1220 + (float)Math.Cos(t) * r, 180 + (float)Math.Sin(t) * r);
            } else
                b = new Vector2(Game.MouseState.X, Game.MouseState.Y);

            ir.RasterizeQuadraticBezier(
                a, b, c, 8, OutlineSize,
                innerColor: Color.White * FillOpacity, 
                outerColor: Color.Black * FillOpacity, 
                outlineColor: Color.Red,
                fill: fs,
                annularRadius: AnnularRadius,
                layer: 3,
                shadow: shadow,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset)
            );

            var verts = GetPolygon(new Vector2(700, 700), 1f, PolygonGap, 4f);

            ir.RasterizePolygon(
                verts, 
                ClosedPolygon, animatedRadius + (ClosedPolygon ? 2 : 6), OutlineSize, 
                Color.Blue * FillOpacity, 
                Color.Green * FillOpacity, 
                Color.Red,
                offset: Animate ? new Vector2(Arithmetic.PulseSine(now / 2f, 0f, 128f)) : Vector2.Zero,
                fill: fs,
                annularRadius: AnnularRadius,
                layer: 5,
                shadow: shadow,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null,
                rampUVOffset: new Vector2(0, RampVOffset),
                orientation: orientation
            );

            if (false)
            foreach (var vert in verts) {
                ir.RasterizeEllipse(vert.Position, Vector2.One * 3, Color.Yellow, layer: 6);
            }

            ir.RasterizeEllipse(a, Vector2.One * 3, Color.Yellow, layer: 6, orientation: orientation);
            ir.RasterizeEllipse(b, Vector2.One * 3, Color.Yellow, layer: 6, orientation: orientation);
            ir.RasterizeEllipse(c, Vector2.One * 3, Color.Yellow, layer: 6, orientation: orientation);

            ir.RasterizeRectangle(new Vector2(Width - 256, 0), new Vector2(Width, 256), 0f, Color.Black, layer: 5, orientation: orientation);
            ir.RasterizeEllipse(
                new Vector2(Width - 128, 128), new Vector2(64f), Color.Orange * 1f, Color.Red * 0f, layer: 6,
                shadow: shadow,
                blendState: RenderStates.RasterShapeMaxBlend,
                orientation: orientation
            );

            var scratchGroup = BatchGroup.ForRenderTarget(
                frame, -9999, Scratch, 
                materialSet: Game.Materials, 
                viewTransform: ViewTransform.CreateOrthographic(Scratch.Width, Scratch.Height)
            );
            var fillColor = Color.White;

            using (scratchGroup) {
                var sir = new ImperativeRenderer(scratchGroup, Game.Materials);
                sir.Clear(color: Color.Black);
                for (float x = 0; x <= 1; x += 0.1f) {
                    for (float y = 0; y <= 1; y += 0.1f) {
                        var pos = new Vector2((x * 100) + x + 2, (y * 100) + y + 2);
                        sir.RasterizeRectangle(
                            pos, pos + new Vector2(7), 
                            radiusCW: new Vector4(3.5f, 3.5f, 0f, 0f), outlineRadius: 1f, 
                            innerColor: fillColor, outerColor: fillColor,
                            outlineColor: Color.White
                        );
                    }
                }
            }

            ir.Draw(Scratch, new Vector2(1200, 700), scale: new Vector2(3), samplerState: SamplerState.PointClamp, layer: 5);

            var fir = new ImperativeRenderer(frame, Game.Materials);
            fir.Draw(RenderTo, Vector2.Zero, layer: 0, blendState: BlendState.Opaque);
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
