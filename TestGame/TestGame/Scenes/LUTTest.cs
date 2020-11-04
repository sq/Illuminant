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

namespace TestGame.Scenes {
    public class LUTTest : Scene {
        Texture2D Background;

        Toggle ApplyLUT;
        Slider LUT2Weight;
        Dropdown<string> LUT1, LUT2;

        public LUTTest (TestGame game, int width, int height)
            : base(game, width, height) {
            ApplyLUT.Key = Keys.A;
            ApplyLUT.Value = true;

            LUT2Weight.Min = 0f;
            LUT2Weight.Max = 1f;
            LUT2Weight.Speed = 0.02f;
            LUT2Weight.Value = 0f;

            LUT1.Value = LUT2.Value = "Identity";
            LUT1.Key = Keys.L;
            LUT2.Key = Keys.OemSemicolon;
        }

        public override void LoadContent () {
            Background = Game.TextureLoader.Load("test pattern");

            var keys = Game.LUTs.Keys.OrderBy(n => n).ToArray();
            LUT1.AddRange(keys);
            LUT2.AddRange(keys);
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var m = Game.Materials.Get(Game.Materials.ScreenSpaceBitmapWithLUT, blendState: BlendState.Opaque);

            var lut1 = Game.LUTs[ApplyLUT ? LUT1.Value : "Identity"];
            var lut2 = Game.LUTs[LUT2.Value];
            var l2w = LUT2Weight.Value;

            Game.RenderCoordinator.BeforePrepare(() => {
                Game.Materials.SetLUTs(m, lut1, lut2, l2w);
            });

            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Clear(layer: 0, color: Color.Black);

            var mc = Color.White;
            if (ApplyLUT)
                ir.Draw(Background, Vector2.Zero, layer: 1, material: m, multiplyColor: mc);
            else
                ir.Draw(Background, Vector2.Zero, layer: 1, blendState: BlendState.Opaque, multiplyColor: mc);

            ir.Draw(lut1, Vector2.Zero, layer: 3, multiplyColor: Color.White, blendState: BlendState.Opaque);
            ir.Draw(lut2, Vector2.Zero, layer: 4, multiplyColor: Color.White * (ApplyLUT ? l2w : 0), blendState: BlendState.AlphaBlend);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
