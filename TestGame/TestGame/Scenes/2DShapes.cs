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
        public Shapes (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        public override void LoadContent () {
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Clear(layer: 0, color: Color.Black);

            ir.RasterizeEllipse(
                Vector2.One * 500, Vector2.One * 420, 4, 
                new Color(0.5f, 0.1f, 0.25f, 1f), 
                new Color(0.1f, 0.1f, 0f, 1f), 
                outlineColor: Color.White, 
                layer: 1
            );

            ir.RasterizeLineSegment(
                new Vector2(32, 32), new Vector2(1024, 64), Vector2.One * 6, 4, 
                Color.White, Color.White,
                outlineColor: Color.Red, 
                layer: 2
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
