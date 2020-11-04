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
    public class PaletteTest : Scene {
        Texture2D PalettedImage, DefaultPaletteTexture, PaletteTexture;
        UInt32[] Palette = new UInt32[256];

        Toggle CyclePalettes;

        public PaletteTest (TestGame game, int width, int height)
            : base(game, width, height) {

            CyclePalettes.Key = Keys.C;
        }

        public override void LoadContent () {
            var options = new TextureLoadOptions {
                Palette = Palette,
                Premultiply = false,
                FloatingPoint = false,
                GenerateMips = false,
                PaletteTextureHeight = 3
            };
            PalettedImage = Game.TextureLoader.Load("paletted-image", options);
            DefaultPaletteTexture = options.PaletteTexture;
            PaletteTexture = Game.TextureLoader.Load("palette", new TextureLoadOptions {
                Premultiply = true,
                GenerateMips = false
            });
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var m = Game.Materials.Get(Game.Materials.ScreenSpacePalettedBitmap, blendState: BlendState.AlphaBlend);

            Game.RenderCoordinator.BeforePrepare(() => {
                m.Parameters.SetPalette(PaletteTexture);
            });

            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Clear(layer: 0, color: Color.DeepSkyBlue);

            var mc = Color.White;
            ir.Draw(PaletteTexture, Vector2.Zero, scale: Vector2.One * 3, layer: 1, blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);

            float p = CyclePalettes ? (float)(Time.Seconds / 8) : 0;
            var userData = new Vector4(p % 1, 0, 0, 0);

            ir.Draw(PalettedImage, new Vector2(0, 3 * PaletteTexture.Height), layer: 1, scale: Vector2.One * 2, multiplyColor: mc, material: m, userData: userData);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
