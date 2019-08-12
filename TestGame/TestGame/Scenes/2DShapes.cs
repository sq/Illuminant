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
using Squared.Util;
using Nuke = NuklearDotNet.Nuklear;

namespace TestGame.Scenes {
    // These aren't illuminant specific but who cares
    public class Shapes : Scene {
        Toggle AnimateRadius, AnimateBezier, BlendInLinearSpace, GradientAlongLine, RadialGradient, Outlines, UseTexture;
        Slider Gamma, ArcLength;

        Texture2D Texture;

        public Shapes (TestGame game, int width, int height)
            : base(game, width, height) {
            Gamma.Min = 0.1f;
            Gamma.Max = 3.0f;
            Gamma.Value = 1.0f;
            Gamma.Speed = 0.1f;
            BlendInLinearSpace.Value = true;
            Outlines.Value = true;
            ArcLength.Min = 5f;
            ArcLength.Max = 180f;
            ArcLength.Value = 45f;
            ArcLength.Speed = 5f;
        }

        public override void LoadContent () {
            Texture = Game.TextureLoader.Load("template");
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var ir = new ImperativeRenderer(frame, Game.Materials, blendState: BlendState.AlphaBlend);
            ir.Clear(layer: 0, color: new Color(0, 32, 48));
            ir.RasterOutlineGamma = Gamma.Value;
            ir.RasterBlendInLinearSpace = BlendInLinearSpace.Value;

            var now = (float)Time.Seconds;

            ir.RasterizeEllipse(
                Vector2.One * 500, Vector2.One * 420, Outlines ? 1f : 0, 
                new Color(0.0f, 0.0f, 0.0f, 1f), 
                new Color(0.2f, 0.2f, 0.2f, 1f), 
                outlineColor: Color.White, 
                layer: 1
            );

            ir.RasterizeLineSegment(
                new Vector2(32, 32), new Vector2(1024, 64), 8, Outlines ? 1f : 0f, 
                Color.White, Color.Black,
                outlineColor: Color.Red,
                gradientAlongLine: GradientAlongLine, 
                layer: 1
            );

            var tl = new Vector2(80, 112);
            var br = new Vector2(512, 400);
            ir.RasterizeRectangle(
                tl, br, (AnimateRadius.Value 
                    ? Arithmetic.PulseSine(now / 3f, 0, 32)
                    : 0f), Outlines ? 6f : 0f, 
                Color.Red, Color.Green,
                outlineColor: Color.Blue,
                radialGradient: RadialGradient,
                layer: 1,
                texture: UseTexture ? Texture : null
            );

            ir.RasterizeRectangle(
                new Vector2(16, 256), new Vector2(16, 512), 8, new Color(1f, 0, 0, 1), new Color(0.5f, 0, 0, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(32, 256), new Vector2(32, 512), 8, new Color(1f, 1f, 0, 1), new Color(0.5f, 0.5f, 0, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(48, 256), new Vector2(48, 512), 8, new Color(0f, 1f, 0, 1), new Color(0f, 0.5f, 0, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(64, 256), new Vector2(64, 512), 8, new Color(0f, 1f, 1f, 1), new Color(0f, 0.5f, 0.5f, 1),
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(80, 256), new Vector2(80, 512), 8, new Color(0f, 0f, 1f, 1), new Color(0f, 0f, 0.5f, 1),
                layer: 2
            );

            ir.RasterizeTriangle(
                new Vector2(640, 96), new Vector2(1200, 256), new Vector2(800, 512), 
                1, Outlines ? 1f : 0,
                Color.Black, Color.White, outlineColor: Color.Blue,
                layer: 2,
                texture: UseTexture ? Texture : null
            );

            ir.RasterizeArc(
                new Vector2(200, 860),
                AnimateBezier ? (float)(Time.Seconds) * 60f : 0f, ArcLength,
                120, 8, Outlines ? 1 : 0,
                Color.White, Color.Black, Color.Blue,
                layer: 2
            );

            Vector2 a = new Vector2(1024, 64),
                b, c = new Vector2(1400, 256);
            if (AnimateBezier) {
                float t = now / 2;
                float r = 140;
                b = new Vector2(1220 + (float)Math.Cos(t) * r, 180 + (float)Math.Sin(t) * r);
            } else
                b = new Vector2(1200, 64);

            ir.RasterizeQuadraticBezier(
                a, b, c, 8, Outlines ? 1f : 0f, Color.White, Color.Black, Color.Red,
                layer: 3
            );

            ir.RasterizeEllipse(a, Vector2.One * 3, Color.Yellow, layer: 4);
            ir.RasterizeEllipse(b, Vector2.One * 3, Color.Yellow, layer: 4);
            ir.RasterizeEllipse(c, Vector2.One * 3, Color.Yellow, layer: 4);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
