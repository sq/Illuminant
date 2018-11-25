using System;
using System.Collections.Generic;
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
    public class LUTTest : Scene {
        Dictionary<string, ColorLUT> LUTs = new Dictionary<string, ColorLUT>();
        Texture2D Background;

        Slider LUT2Weight;
        [Items("Identity")]
        [Items("Darken")]
        [Items("Invert")]
        [Items("GammaHalf")]
        Dropdown<string> LUT1;
        [Items("Identity")]
        [Items("Darken")]
        [Items("Invert")]
        [Items("GammaHalf")]
        Dropdown<string> LUT2;

        public LUTTest (TestGame game, int width, int height)
            : base(game, width, height) {
            LUT2Weight.Min = 0f;
            LUT2Weight.Max = 1f;
            LUT2Weight.Speed = 0.1f;
            LUT2Weight.Value = 0f;

            LUT1.Key = Keys.L;
            LUT2.Key = Keys.OemSemicolon;
        }

        public override void LoadContent () {
            Background = Game.Content.Load<Texture2D>("vector-field-background");

            LoadLUT("Identity");
            LoadLUT("Darken");
            LoadLUT("Invert");
            LoadLUT("GammaHalf");
        }

        private void LoadLUT (string name) {
            var texture = Game.Content.Load<Texture2D>("lut-" + name);
            var lut = new ColorLUT(texture, 4, 4);
            LUTs.Add(name, lut);
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            var m = Game.Materials.Get(Game.Materials.ScreenSpaceBitmapWithLUT, blendState: BlendState.Opaque);

            Game.RenderCoordinator.BeforePrepare(() => {
                m.Effect.Parameters["LUT1"].SetValue(LUTs[LUT1.Value]);
                m.Effect.Parameters["LUT2"].SetValue(LUTs[LUT2.Value]);
                m.Effect.Parameters["LUT2Weight"].SetValue(LUT2Weight.Value);
            });

            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Clear(layer: 0, color: Color.Black);

            ir.Draw(Background, Vector2.Zero, layer: 1, material: m);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
