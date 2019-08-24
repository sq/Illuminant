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
    public class RasterShapeSpeed : Scene {
        Toggle BlendInLinearSpace, UseTexture, UseGeometry;

        Texture2D Texture;

        public RasterShapeSpeed (TestGame game, int width, int height)
            : base(game, width, height) {
        }

        public override void LoadContent () {
            Texture = Game.TextureLoader.Load("template");
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var vt = Game.Materials.ViewTransform;
            vt.Position = new Vector2(64, 64);
            vt.Scale = Vector2.One * 1.2f;

            var batch = BatchGroup.New(
                frame, 0,
                materialSet: Game.Materials,
                viewTransform: vt
            );

            var ir = new ImperativeRenderer(batch, Game.Materials, blendState: BlendState.AlphaBlend);
            ir.Clear(layer: 0, color: new Color(0, 32, 48));
            ir.RasterBlendInLinearSpace = BlendInLinearSpace.Value;

            for (int y = 0; y < 32; y++) {
                for (int x = 0; x < 32; x++) {
                    var center = new Vector2(x * 48, y * 48);
                    var radius = Vector2.One * (24 + (x + y) / 2);

                    if (UseGeometry)
                        ir.FillCircle(center, 0, radius.X, Color.White, Color.Black);
                    else
                        ir.RasterizeEllipse(center, radius, Color.White, Color.Black);
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
