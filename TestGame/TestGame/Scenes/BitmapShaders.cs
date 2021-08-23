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
    public class BitmapShaders : Scene {
        Texture2D TestImage, TestImage2;

        [Items("Normal")]
        [Items("Shadowed")]
        [Items("Stippled")]
        [Items("HorizontalBlur")]
        [Items("VerticalBlur")]
        [Items("RadialBlur")]
        [Items("HighlightColor")]
        [Items("Crossfade")]
        [Items("Over")]
        [Items("Under")]
        Dropdown<string> Shader;

        Slider Opacity, Brightness, ShadowOffset, DitherGamma, StippleRatio, 
            BlurSigma, BlurSampleRadius, HighlightTolerance, Image2Weight;

        Toggle PreserveAspectRatio;

        public BitmapShaders (TestGame game, int width, int height)
            : base(game, width, height) {

            Opacity.Min = 0f;
            Opacity.Max = 1;
            Opacity.Speed = 0.01f;
            Opacity.Value = 1;
            Brightness.Min = -1f;
            Brightness.Max = 1;
            Brightness.Speed = 0.01f;
            Brightness.Value = 1;
            ShadowOffset.Max = 16;
            ShadowOffset.Speed = 0.5f;
            ShadowOffset.Value = 4;
            ShadowOffset.Min = -2;
            DitherGamma.Min = 0.1f;
            DitherGamma.Max = 3f;
            DitherGamma.Value = 1f;
            DitherGamma.Speed = 0.01f;
            StippleRatio.Min = 1f;
            StippleRatio.Max = 10f;
            StippleRatio.Speed = 0.5f;
            StippleRatio.Value = 1f;
            BlurSigma.Min = 0.1f;
            BlurSigma.Max = 7.0f;
            BlurSigma.Value = 2f;
            BlurSigma.Speed = 0.05f;
            BlurSampleRadius.Integral = true;
            BlurSampleRadius.Min = 1;
            BlurSampleRadius.Max = 9;
            BlurSampleRadius.Value = 3;
            BlurSampleRadius.Speed = 1;
            HighlightTolerance.Max = 2;
            HighlightTolerance.Min = 0;
            HighlightTolerance.Value = 0.1f;
            HighlightTolerance.Speed = 0.01f;
            Image2Weight.Min = 0;
            Image2Weight.Max = 1;
            Image2Weight.Value = 0.5f;
            Image2Weight.Speed = 0.05f;
            PreserveAspectRatio.Value = true;
        }

        public override void LoadContent () {
            TestImage = Game.TextureLoader.Load("transparent_test");
            TestImage2 = Game.TextureLoader.Load("precision-test");
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            Vector4 userData = default(Vector4);
            Material material;
            switch (Shader.Value) {
                case "Shadowed":
                    // HACK: Ensure we don't trample the default global shadow settings (-:
                    material = Game.Materials.WorldSpaceShadowedBitmap;
                    // FIXME: why is this broken?
                    userData = new Vector4(32 / 255f * 0.8f, 0, 0, 0.8f);
                    break;
                case "Stippled":
                    material = Game.Materials.ScreenSpaceStippledBitmap;
                    userData = new Vector4(0, DitherGamma, StippleRatio - 1, 0);
                    break;
                case "HorizontalBlur":
                    material = Game.Materials.ScreenSpaceHorizontalGaussianBlur;
                    break;
                case "VerticalBlur":
                    material = Game.Materials.ScreenSpaceVerticalGaussianBlur;
                    break;
                case "RadialBlur":
                    material = Game.Materials.ScreenSpaceRadialGaussianBlur;
                    break;
                case "HighlightColor":
                    material = Game.Materials.HighlightColorBitmap;
                    var c = new Color(42, 8, 25);
                    userData = c.ToVector4();
                    userData.W = HighlightTolerance;
                    break;
                case "Crossfade":
                    material = Game.Materials.CrossfadeBitmap;
                    userData = new Vector4(Image2Weight.Value);
                    break;
                case "Over":
                    material = Game.Materials.OverBitmap;
                    userData = new Vector4(Image2Weight.Value);
                    break;
                case "Under":
                    material = Game.Materials.UnderBitmap;
                    userData = new Vector4(Image2Weight.Value);
                    break;
                default:
                    material = Game.Materials.ScreenSpaceBitmap;
                    break;
            }

            var ir = new ImperativeRenderer(frame, Game.Materials);

            material = Game.Materials.Get(material, blendState: BlendState.AlphaBlend);
            Game.Materials.SetGaussianBlurParameters(material, BlurSigma, (int)(BlurSampleRadius) * 2 + 1);
            ir.Parameters.Add("ShadowOffset", new Vector2(ShadowOffset * 0.66f, ShadowOffset));

            ir.Clear(layer: 0, color: Color.DeepSkyBlue * 0.33f);

            var white = (int)(255 * Brightness);
            var multiplyColor = new Color(
                white, white, white, 255
            ) * Opacity;

            var dc = new BitmapDrawCall(TestImage, Vector2.Zero) {
                Texture2 = TestImage2,
                MultiplyColor = multiplyColor,
                UserData = userData
            };
            dc.AlignTexture2(2.0f, preserveAspectRatio: PreserveAspectRatio.Value);

            ir.Draw(
                dc, layer: 1, material: material,
                samplerState2: SamplerState.PointWrap
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
