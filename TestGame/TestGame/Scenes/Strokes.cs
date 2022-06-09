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
using Squared.Render.RasterStroke;
using Squared.Util;

namespace TestGame.Scenes {
    // These aren't illuminant specific but who cares
    public class Strokes : Scene {
        Toggle Animate, BlendInLinearSpace, WorldSpace, ClosedPolygon;

        // Dropdown<string> Brush;

        [Group("Brush Properties")]
        Slider Spacing, Size, RotationRate, Flow;

        [Group("Dynamics")]
        Slider TaperIn, TaperOut;

        Texture2D NozzleAtlas, FillPattern;

        public Strokes (TestGame game, int width, int height)
            : base(game, width, height) {
            Spacing.Min = 0.05f;
            Spacing.Max = 2f;
            Spacing.Value = 0.2f;
            Spacing.Speed = 0.05f;
            Size.Min = 16f;
            Size.Max = 256f;
            Size.Value = 64f;
            Size.Speed = 4f;
            RotationRate.Min = -180f;
            RotationRate.Max = 180f;
            RotationRate.Value = 2f;
            RotationRate.Speed = 1f;
            Flow.Min = 0.05f;
            Flow.Max = 1.0f;
            Flow.Value = 1.0f;
            Flow.Speed = 0.05f;
            BlendInLinearSpace.Value = true;
        }

        public override void LoadContent () {
            NozzleAtlas = Game.TextureLoader.Load(
                "acrylic-nozzles", 
                new TextureLoadOptions { Premultiply = false, GenerateMips = true }, 
                cached: true
            );
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

        public override void Draw (Squared.Render.Frame frame) {
            var now = (float)Time.Seconds;

            var brush = new RasterBrush {
                NozzleAtlas = NozzleAtlas,
                NozzleCountX = 2,
                NozzleCountY = 2,
                Size = Size.Value,
                Spacing = Spacing.Value,
                RotationRateDegrees = RotationRate.Value,
                Flow = Flow.Value
            };

            var ir = new ImperativeRenderer(frame, Game.Materials, blendState: BlendState.NonPremultiplied);
            ir.RasterBlendInLinearSpace = BlendInLinearSpace.Value;
            ir.Clear(layer: 0, color: new Color(0, 96, 128));
            ir.WorldSpace = WorldSpace;

            float animatedRadius = (Animate.Value
                    ? Arithmetic.PulseSine(now / 3f, 2, 32)
                    : 2f);

            var tl = new Vector2(80, 112);
            var br = new Vector2(1024, 800);

            ir.StrokeLineSegment(
                tl, br, Color.White, Color.Black, brush
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
