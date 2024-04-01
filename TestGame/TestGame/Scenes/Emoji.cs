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
using Squared.PRGUI.Controls;
using Squared.PRGUI.Imperative;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.RasterShape;
using Squared.Render.RasterStroke;
using Squared.Render.Text;
using Squared.Util;

namespace TestGame.Scenes {
    public class Emoji : Scene {
        public const string TestText = "🩷💀🫱🏿‍🫲🏻🌴🐢🐐🍄⚽🫧👑📸🪼👀🚨🏡🕊️🏆😻🌟🧿🍀🫶🏾🍜";

        [Group("Font")]
        Slider BaseSize, TextSize;

        private FreeTypeFont Font;
        private FreeTypeFont.FontSize FontSize;
        private Material TextMaterial;

        public Emoji (TestGame game, int width, int height)
            : base(game, width, height) {
            BaseSize.Min = 4f;
            BaseSize.Max = 64f;
            BaseSize.Value = 32f;
            BaseSize.Speed = 1f;
            TextSize.Min = 2f;
            TextSize.Max = 128f;
            TextSize.Value = 64f;
            TextSize.Speed = 1f;
        }

        public override void LoadContent () {
            Font = Game.FontLoader.Load("seguiemj");
            // argb glyphs are provided by freetype in premultiplied sRGB
            Font.Format = FreeTypeFontFormat.SRGB;
            Font.GlyphMargin = 5;
            FontSize = new FreeTypeFont.FontSize(Font, BaseSize.Value) {
            };
            TextMaterial = (Game.PRGUIContext.Decorations as Squared.PRGUI.DefaultDecorations).TextMaterial.Clone();
        }

        public override void UnloadContent () {
        }

        public override void Draw (Frame frame) {
            var now = (float)Time.Seconds;

            FontSize.SizePoints = BaseSize.Value;
            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Parameters.Add("GlobalShadowColor", Color.Red);
            ir.Clear(color: new Color(0, 48, 64));
            ir.DrawString(
                FontSize, TestText, new Vector2(0, 16), scale: TextSize.Value / BaseSize.Value, material: TextMaterial,
                alignToPixels: false, blendState: BlendState.AlphaBlend
            );
        }

        public override void Update (GameTime gameTime) {
            Game.IsMouseVisible = true;
        }
    }
}
