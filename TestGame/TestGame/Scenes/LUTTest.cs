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
        Slider LUT2Weight, LUTIndex1, LUTIndex2;
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

            LUTIndex2.Min = LUTIndex1.Min = 0;
            LUTIndex2.Max = LUTIndex1.Max = 0;
            LUTIndex2.Integral = LUTIndex1.Integral = true;
        }

        public override void LoadContent () {
            Background = Game.TextureLoader.Load("hero1");

            // FIXME: Why are the LUTs all making this image darker?
            var keys = Game.LUTs.Keys.OrderBy(n => n).ToArray();
            LUT1.Clear();
            LUT2.Clear();
            LUT1.AddRange(keys);
            LUT2.AddRange(keys);
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var m = Game.Materials.Get(Game.Materials.BitmapWithLUT, blendState: BlendState.Opaque);

            var lut1 = Game.LUTs[ApplyLUT ? LUT1.Value : "Identity"];
            var lut2 = Game.LUTs[LUT2.Value];
            LUTIndex1.Max = lut1?.RowCount - 1;
            LUTIndex2.Max = lut2?.RowCount - 1;
            var l2w = LUT2Weight.Value;

            Game.RenderCoordinator.BeforePrepare(() => {
                Game.Materials.SetLUTs(m, lut1, lut2, l2w, (int)LUTIndex1.Value, (int)LUTIndex2.Value);
            });

            var ir = new ImperativeRenderer(frame, Game.Materials, samplerState: SamplerState.PointClamp);
            ir.Clear(layer: 0, color: Color.Black);

            var mc = Color.White;
            if (ApplyLUT)
                ir.Draw(Background, Vector2.Zero, layer: 1, material: m, multiplyColor: mc, scale: Vector2.One * 3);
            else
                ir.Draw(Background, Vector2.Zero, layer: 1, blendState: BlendState.Opaque, multiplyColor: mc, scale: Vector2.One * 3);

            var srcRect1 = new Rectangle(0, (int)LUTIndex1.Value * lut1.Resolution, lut1.Texture.Width, lut1.Resolution);
            var srcRect2 = new Rectangle(0, (int)LUTIndex2.Value * lut2.Resolution, lut2.Texture.Width, lut2.Resolution);
            ir.Draw(lut1, Vector2.Zero, layer: 3, multiplyColor: Color.White, blendState: BlendState.Opaque, sourceRectangle: srcRect1);
            ir.Draw(lut2, Vector2.Zero, layer: 4, multiplyColor: Color.White * (ApplyLUT ? l2w : 0), blendState: BlendState.AlphaBlend, sourceRectangle: srcRect2);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
