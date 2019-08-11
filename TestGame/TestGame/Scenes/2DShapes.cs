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
        Toggle AnimateRadius, BlendInLinearSpace, GradientAlongLine, RadialGradient, Outlines;
        Slider Gamma;

        public Shapes (TestGame game, int width, int height)
            : base(game, width, height) {
            Gamma.Min = 0.1f;
            Gamma.Max = 3.0f;
            Gamma.Value = 1.5f;
            Gamma.Speed = 0.1f;
        }

        public override void LoadContent () {
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var ir = new ImperativeRenderer(frame, Game.Materials, blendState: BlendState.AlphaBlend);
            ir.Clear(layer: 0, color: new Color(0, 32, 48));
            ir.RasterOutlineGamma = Gamma.Value;
            ir.RasterBlendInLinearSpace = BlendInLinearSpace.Value;

            ir.RasterizeEllipse(
                Vector2.One * 500, Vector2.One * 420, Outlines ? 1f : 0, 
                new Color(0.0f, 0.0f, 0.0f, 1f), 
                new Color(0.1f, 0.1f, 0.1f, 1f), 
                outlineColor: Color.White, 
                layer: 1
            );

            ir.RasterizeLineSegment(
                new Vector2(32, 32), new Vector2(1024, 64), Vector2.One * 6, Outlines ? 1.5f : 0f, 
                Color.White, Color.Black,
                outlineColor: Color.Red,
                gradientAlongLine: GradientAlongLine, 
                layer: 2
            );

            var tl = new Vector2(64, 96);
            var br = new Vector2(512, 400);
            ir.RasterizeRectangle(
                tl, br, Vector2.One * (AnimateRadius.Value 
                    ? Arithmetic.PulseSine((float)Time.Seconds / 3f, 0, 32)
                    : 0f), Outlines ? 6f : 0f, 
                Color.Red, Color.Green,
                outlineColor: Color.Blue,
                radialGradient: RadialGradient,
                layer: 2
            );

            ir.RasterizeRectangle(
                new Vector2(16, 256), new Vector2(16, 512), Vector2.One * 4, new Color(0.5f, 0, 0, 1), new Color(0.5f, 0, 0, 1),
                layer: 3
            );

            ir.RasterizeRectangle(
                new Vector2(32, 256), new Vector2(32, 512), Vector2.One * 4, new Color(0.5f, 0.5f, 0, 1), new Color(0.5f, 0.5f, 0, 1),
                layer: 3
            );

            ir.RasterizeRectangle(
                new Vector2(48, 256), new Vector2(48, 512), Vector2.One * 4, new Color(0f, 0.5f, 0, 1), new Color(0f, 0.5f, 0, 1),
                layer: 3
            );

            ir.RasterizeRectangle(
                new Vector2(64, 256), new Vector2(64, 512), Vector2.One * 4, new Color(0f, 0.5f, 0.5f, 1), new Color(0f, 0.5f, 0.5f, 1),
                layer: 3
            );

            ir.RasterizeRectangle(
                new Vector2(80, 256), new Vector2(80, 512), Vector2.One * 4, new Color(0f, 0f, 0.5f, 1), new Color(0f, 0f, 0.5f, 1),
                layer: 3
            );

            ir.RasterizeTriangle(
                new Vector2(640, 96), new Vector2(1200, 256), new Vector2(800, 512), 
                Vector2.One * 1, Outlines ? 2f : 0,
                Color.Black, Color.White, outlineColor: Color.Blue,
                layer: 4
            );
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
