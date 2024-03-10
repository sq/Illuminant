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
    public class SDFText : Scene {
        public const string TestText = "The quick brown fox jumped over the lazy dogs.\r\n" +
            "Sphinx of Black Quartz, Judge My Vow!\r\n" +
            "0123456789 -+/*\\%$";

        [Group("Distance")]
        Slider Scale, Offset, Power;

        [Group("Outline")]
        Slider OutlineThickness, OutlineSoftness, OutlinePower, OutlineOffset;

        [Group("Font")]
        Slider BaseSize, TextSize;
        Toggle MipMaps;

        private FreeTypeFont.FontSize FontSize;
        private Material TextMaterial;

        public SDFText (TestGame game, int width, int height)
            : base(game, width, height) {
            Scale.Min = 0.01f;
            Scale.Max = 2f;
            Scale.Value = 0.25f;
            Scale.Speed = 0.01f;
            Offset.Min = -10f;
            Offset.Max = 10f;
            Offset.Value = 1.0f;
            Offset.Speed = 0.25f;
            Power.Min = 0.5f;
            Power.Max = 4f;
            Power.Value = 1.8f;
            Power.Speed = 0.05f;
            BaseSize.Min = 4f;
            BaseSize.Max = 64f;
            BaseSize.Value = 32f;
            BaseSize.Speed = 1f;
            TextSize.Min = 2f;
            TextSize.Max = 128f;
            TextSize.Value = 64f;
            TextSize.Speed = 1f;
            OutlineThickness.Min = -32f;
            OutlineThickness.Max = 256f;
            OutlineThickness.Value = 8f;
            OutlineThickness.Speed = 0.5f;
            OutlineSoftness.Min = 0.1f;
            OutlineSoftness.Max = 128f;
            OutlineSoftness.Value = 4f;
            OutlineSoftness.Speed = 0.05f;
            OutlinePower.Min = 0.05f;
            OutlinePower.Max = 4f;
            OutlinePower.Value = 1f;
            OutlinePower.Speed = 0.1f;
            OutlineOffset.Min = -32f;
            OutlineOffset.Max = 32f;
            OutlineOffset.Value = 0f;
            OutlineOffset.Speed = 0.5f;
            MipMaps.Key = Keys.M;
        }

        public override void LoadContent () {
            FontSize = new FreeTypeFont.FontSize(Game.Font, BaseSize.Value) {
                OverrideFormat = FreeTypeFontFormat.DistanceField,
            };
            TextMaterial = Game.Materials.Get(Game.Materials.DistanceFieldText, depthStencilState: RenderStates.OutlinedTextDepthStencil, clone: true);
        }

        public override void UnloadContent () {
        }

        public override void Draw (Frame frame) {
            var now = (float)Time.Seconds;

            if (MipMaps.Value != Game.Font.SDFMipMapping) {
                Game.Font.SDFMipMapping = MipMaps.Value;
                FontSize.Dispose();
                FontSize = new FreeTypeFont.FontSize(Game.Font, BaseSize.Value) {
                    OverrideFormat = FreeTypeFontFormat.DistanceField,
                };
            } else
                FontSize.SizePoints = BaseSize.Value;
            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Parameters.Add("GlobalShadowColor", Color.Red);
            ir.Parameters.Add("ShadowOffset", new Vector2(OutlineOffset.Value, OutlineOffset.Value));
            ir.Parameters.Add("TextDistanceScaleOffsetAndPower", new Vector3(Scale.Value, Offset.Value, Power.Value));
            ir.Parameters.Add("OutlineRadiusSoftnessAndPower", new Vector3(OutlineThickness.Value, OutlineSoftness.Value, OutlinePower.Value));
            ir.Clear(color: Color.CornflowerBlue, z: 0f);
            ir.DrawString(
                FontSize, TestText, Vector2.One, scale: TextSize.Value / BaseSize.Value, material: TextMaterial,
                alignToPixels: false, blendState: BlendState.AlphaBlend
            );
        }

        public override void Update (GameTime gameTime) {
            Game.IsMouseVisible = true;
        }
    }
}
