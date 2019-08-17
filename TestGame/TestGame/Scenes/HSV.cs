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
    public class HueTest : Scene {
        Texture2D TestPattern;

        Toggle ApplyShader, Sepia;

        Slider Hue, Saturation, Luminance, SepiaWeight;

        public HueTest (TestGame game, int width, int height)
            : base(game, width, height) {

            Sepia.Key = Keys.S;
            ApplyShader.Key = Keys.A;
            ApplyShader.Value = true;
            Hue.Min = -360;
            Hue.Max = 360;
            Hue.Speed = 5;
            Saturation.Min = -1;
            Saturation.Max = 1;
            Saturation.Speed = 0.01f;
            Luminance.Min = -1;
            Luminance.Max = 1;
            Luminance.Speed = 0.01f;
            SepiaWeight.Min = 0;
            SepiaWeight.Max = 2;
            SepiaWeight.Speed = 0.01f;
            SepiaWeight.Value = 1;
        }

        public override void LoadContent () {
            TestPattern = Game.TextureLoader.Load("test pattern");
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var baseMaterial = ApplyShader 
                ? (
                    Sepia
                        ? Game.Materials.ScreenSpaceSepiaBitmap
                        : Game.Materials.ScreenSpaceHueBitmap 
                  )
                : Game.Materials.ScreenSpaceBitmap;
            var m = Game.Materials.Get(baseMaterial, blendState: BlendState.AlphaBlend);

            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Clear(layer: 0, color: Color.DeepSkyBlue);

            var mc = Color.White;

            var userData = new Vector4(
                Hue / 360, 
                Sepia ? Saturation + 0.5f : Saturation, 
                Sepia ? Luminance + 0.5f : Luminance, 
                SepiaWeight
            );

            ir.Draw(TestPattern, Vector2.Zero, layer: 1, scale: Vector2.One, multiplyColor: mc, material: m, userData: userData);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
