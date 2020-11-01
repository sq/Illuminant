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
using Nuke = NuklearDotNet.Nuklear;

namespace TestGame.Scenes {
    // These aren't illuminant specific but who cares
    public class Shapes : Scene {
        Toggle Animate, BlendInLinearSpace, WorldSpace;
        Slider ArcLength, AnnularRadius;

        [Group("Outline")]
        Toggle HardOutlines;
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
        [Items("Horizontal")]
        [Items("Vertical")]
        [Items("Angular")]
        Dropdown<string> FillMode;

        [Group("Fill")]
        Toggle RepeatFill, GradientAlongLine, UseRamp;
        [Group("Fill")]
        Slider FillOffset, FillSize, FillAngle;

        [Group("Shadow")]
        Toggle ShadowInside;
        [Group("Shadow")]
        Slider ShadowSoftness, ShadowOffset, ShadowOpacity, ShadowExpansion;

        [Group("Texture")]
        Toggle UseTexture, CompositeTexture, PreserveAspectRatio;
        [Group("Texture")]
        Slider TextureSize, TextureOrigin, TexturePosition;

        Texture2D Texture, RampTexture;

        AutoRenderTarget Scratch, RenderTo;

        public Shapes (TestGame game, int width, int height)
            : base(game, width, height) {
            Gamma.Min = 0.1f;
            Gamma.Max = 3.0f;
            Gamma.Value = 1.0f;
            Gamma.Speed = 0.1f;
            BlendInLinearSpace.Value = true;
            OutlineSize.Min = 0f;
            OutlineSize.Max = 10f;
            OutlineSize.Value = 1f;
            OutlineSize.Speed = 0.5f;
            ArcLength.Min = 5f;
            ArcLength.Max = 360f;
            ArcLength.Value = 45f;
            ArcLength.Speed = 5f;
            HardOutlines.Value = true;
            FillMode.Value = "Natural";
            FillOffset.Min = -1f;
            FillOffset.Max = 1f;
            FillOffset.Speed = 0.1f;
            FillSize.Value = 1f;
            FillSize.Min = 0.05f;
            FillSize.Max = 4f;
            FillSize.Speed = 0.05f;
            AnnularRadius.Value = 0f;
            AnnularRadius.Min = 0f;
            AnnularRadius.Max = 32f;
            AnnularRadius.Speed = 0.25f;
            FillAngle.Min = 0f;
            FillAngle.Max = 360f;
            FillAngle.Speed = 2f;
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
            ShadowExpansion.Min = -2f;
            ShadowExpansion.Max = 8f;
            ShadowExpansion.Speed = 0.1f;
            TextureSize.Value = 1;
            TextureSize.Max = 4;
            TextureSize.Min = 0.1f;
            TextureSize.Speed = 0.1f;
            TextureOrigin.Max = 1;
            TextureOrigin.Speed = 0.01f;
            TexturePosition.Min = -4;
            TexturePosition.Max = 4;
            TexturePosition.Speed = 0.01f;
        }

        public override void LoadContent () {
            Texture = Game.TextureLoader.Load(
                "shape-texture", 
                new TextureLoadOptions { Premultiply = true, GenerateMips = true }, 
                cached: true
            );
            RampTexture = Game.TextureLoader.Load("custom-gradient");
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

            var fillSize = FillSize * (RepeatFill ? -1 : 1);
            var fillMode = (RasterFillMode)Enum.Parse(typeof(RasterFillMode), FillMode.Value);

            var ir = new ImperativeRenderer(batch, Game.Materials, blendState: BlendState.NonPremultiplied);
            ir.Clear(layer: 0, color: new Color(0, 96, 128));
            ir.RasterOutlineGamma = Gamma.Value;
            ir.RasterBlendInLinearSpace = BlendInLinearSpace.Value;
            ir.RasterSoftOutlines = !HardOutlines.Value;
            ir.WorldSpace = WorldSpace;

            ir.RasterShadow.Color = new pSRGBColor(0.4f, 0.02f, 0.22f, 1f) * ShadowOpacity;
            ir.RasterShadow.Softness = ShadowSoftness;
            ir.RasterShadow.Offset = new Vector2(ShadowOffset);
            ir.RasterShadow.Inside = ShadowInside;
            ir.RasterShadow.Expansion = ShadowExpansion;

            var textureSettings = new RasterTextureSettings {
                Mode = CompositeTexture ? RasterTextureCompositeMode.Over : RasterTextureCompositeMode.Multiply,
                PreserveAspectRatio = PreserveAspectRatio,
                Origin = new Vector2(TextureOrigin.Value),
                Scale = new Vector2(TextureSize.Value),
                Position = new Vector2(TexturePosition.Value)
            };

            var now = (float)Time.Seconds;

            ir.RasterizeEllipse(
                Vector2.One * 600, new Vector2(420, 360), OutlineSize,
                innerColor: Color.White,
                outerColor: Color.Black,
                outlineColor: Color.White,
                layer: 1,
                fillMode: fillMode,
                fillOffset: FillOffset,
                fillSize: fillSize,
                fillAngle: FillAngle,
                annularRadius: AnnularRadius,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null
            );

            ir.RasterizeLineSegment(
                new Vector2(32, 32), new Vector2(1024, 64), 1, 8, OutlineSize, 
                innerColor: Color.White, 
                outerColor: Color.Black, 
                outlineColor: Color.Red,
                fillMode: fillMode,
                fillOffset: FillOffset,
                fillSize: fillSize,
                fillAngle: FillAngle,
                annularRadius: AnnularRadius,
                gradientAlongLine: GradientAlongLine, 
                rampTexture: UseRamp ? RampTexture : null,
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
                innerColor: Color.White, 
                outerColor: Color.Black, 
                outlineColor: Color.Blue,
                fillMode: fillMode,
                fillOffset: FillOffset,
                fillSize: fillSize,
                fillAngle: FillAngle,
                annularRadius: AnnularRadius,
                layer: 1,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null
            );

            ir.RasterizeRectangle(
                new Vector2(32, 256), new Vector2(32 + 6, 512), 4.5f, new Color(1f, 0, 0, 1), new Color(0.5f, 0, 0, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(48, 256), new Vector2(48 + 6, 512), 4.5f, new Color(1f, 1f, 0, 1), new Color(0.5f, 0.5f, 0, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(64, 256), new Vector2(64 + 6, 512), 4.5f, new Color(0f, 1f, 0, 1), new Color(0f, 0.5f, 0, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(80, 256), new Vector2(80 + 6, 512), 4.5f, new Color(0f, 1f, 1f, 1), new Color(0f, 0.5f, 0.5f, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(96, 256), new Vector2(96 + 6, 512), 4.5f, new Color(0f, 0f, 1f, 1), new Color(0f, 0f, 0.5f, 1),
                layer: 2
            );

            ir.RasterizeTriangle(
                new Vector2(640, 96), new Vector2(1200, 256), new Vector2(800, 512), 
                animatedRadius, OutlineSize,
                innerColor: Color.White, 
                outerColor: Color.Black, 
                outlineColor: Color.Blue,
                fillMode: fillMode,
                fillOffset: FillOffset,
                fillSize: fillSize,
                fillAngle: FillAngle,
                annularRadius: AnnularRadius,
                layer: 2,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null
            );

            ir.RasterizeEllipse(new Vector2(200, 860), Vector2.One * 3, Color.Yellow, layer: 4);

            ir.RasterizeArc(
                new Vector2(200, 860),
                Animate ? (float)(Time.Seconds) * 60f : 0f, ArcLength,
                120, 8, OutlineSize,
                innerColor: Color.White, 
                outerColor: Color.Black, 
                outlineColor: Color.Blue,
                fillMode: fillMode,
                fillOffset: FillOffset,
                fillSize: fillSize,
                fillAngle: FillAngle,
                annularRadius: AnnularRadius,
                layer: 2,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null
            );

            Vector2 a = new Vector2(1024, 64),
                b, c = new Vector2(1400, 256);
            if (Animate) {
                float t = now / 2;
                float r = 160;
                b = new Vector2(1220 + (float)Math.Cos(t) * r, 180 + (float)Math.Sin(t) * r);
            } else
                b = new Vector2(1200, 64);

            ir.RasterizeQuadraticBezier(
                a, b, c, 8, OutlineSize,
                innerColor: Color.White, 
                outerColor: Color.Black, 
                outlineColor: Color.Red,
                fillMode: fillMode,
                fillOffset: FillOffset,
                fillSize: fillSize,
                fillAngle: FillAngle,
                annularRadius: AnnularRadius,
                layer: 3,
                texture: UseTexture ? Texture : null,
                textureSettings: textureSettings,
                rampTexture: UseRamp ? RampTexture : null
            );

            ir.RasterShadow = default(RasterShadowSettings);

            ir.RasterizeEllipse(a, Vector2.One * 3, Color.Yellow, layer: 4);
            ir.RasterizeEllipse(b, Vector2.One * 3, Color.Yellow, layer: 4);
            ir.RasterizeEllipse(c, Vector2.One * 3, Color.Yellow, layer: 4);

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

            ir.Draw(Scratch, new Vector2(900, 700), scale: new Vector2(3), samplerState: SamplerState.PointClamp, layer: 5);

            var fir = new ImperativeRenderer(frame, Game.Materials);
            fir.Draw(RenderTo, Vector2.Zero, layer: 0, blendState: BlendState.Opaque);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
