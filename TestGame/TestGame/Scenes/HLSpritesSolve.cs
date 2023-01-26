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
using Squared.Util;

namespace TestGame.Scenes {
    public class HLSpritesSolve : Scene {
        [Items("Left")]
        [Items("Right")]
        [Items("Up")]
        [Items("Down")]
        [Items("Normals")]
        [Items("Lightmap")]
        [Items("GBuffer")]
        Dropdown<string> ViewMode;

        [Group("Generator Settings")]
        // Toggle HighPrecisionNormals;
        bool HighPrecisionNormals = false;
        [Group("Generator Settings")]
        Slider NumberOfInputs, ZBasis;

        [Group("Light Settings")]
        Slider LightPosX, LightPosY, LightPosZ;

        private Texture2D InputLeft, InputRight, InputAbove, InputBelow;
        private Texture2D[] Inputs;
        private float[][] InputBuffers;
        private Vector4[] OutputBuffer;
        // private AutoRenderTarget GeneratedMap;
        private Texture2D GeneratedMap;
        private IlluminantMaterials IlluminantMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;
        float LightZ;

        public HLSpritesSolve (TestGame game, int width, int height)
            : base(game, width, height) {
            ViewMode.Value = "Normals";
            NumberOfInputs.Min = 1;
            NumberOfInputs.Max = 4;
            NumberOfInputs.Integral = true;
            NumberOfInputs.Value = 4;
            ZBasis.Min = 0.1f;
            ZBasis.Max = 2f;
            ZBasis.Value = 0.5f;
            LightPosX.Min = LightPosY.Min = LightPosZ.Min = -512f;
            LightPosX.Max = Width;
            LightPosY.Max = Height;
            LightPosZ.Max = 512f;
            LightPosX.Speed = LightPosY.Speed = LightPosZ.Speed = 16f;
        }

        public override void LoadContent () {
            InputBelow = Game.TextureLoader.LoadSync("normalgen-below", new TextureLoadOptions {
            }, true, false);
            InputAbove = Game.TextureLoader.LoadSync("normalgen-above", new TextureLoadOptions {
            }, true, false);
            InputLeft = Game.TextureLoader.LoadSync("normalgen-left", new TextureLoadOptions {
            }, true, false);
            InputRight = Game.TextureLoader.LoadSync("normalgen-right", new TextureLoadOptions {
            }, true, false);
            Inputs = new [] { InputLeft, InputRight, InputAbove, InputBelow };
            InputBuffers = new float[Inputs.Length][];
            for (int i = 0; i < Inputs.Length; i++)
                InputBuffers[i] = AwfulReadback(Inputs[i]);
            OutputBuffer = new Vector4[InputLeft.Width * InputLeft.Height];

            Environment = new LightingEnvironment();
            Environment.GroundZ = 0;
            Environment.MaximumZ = 512;
            Environment.ZToYMultiplier = 0f;
            Environment.Ambient = new Color(63, 63, 63, 0);
            Environment.Lights.Add(new SphereLightSource {
                CastsShadows = false, Color = new Vector4(0.5f, 0.1f, 0.2f, 1f),
                Radius = 64f,
                RampLength = 800f,
                RampMode = LightSourceRampMode.Exponential
            });

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment,
                new RendererConfiguration(
                    Width, Height, true, enableBrightnessEstimation: false, stencilCulling: false
                ) {
                    MaximumFieldUpdatesPerFrame = 3,
                    DefaultQuality = {
                        MinStepSize = 2f,
                        LongStepFactor = 0.8f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = true,
                }, Game.IlluminantMaterials
            );
            IlluminantMaterials = Renderer.IlluminantMaterials;

            Renderer.OnRenderGBuffer += (LightingRenderer lr, ref ImperativeRenderer ir) => {
                ir.Parameters.Add("NormalsAreSigned", HighPrecisionNormals);
                ir.Draw(GeneratedMap, Vector2.Zero, material: IlluminantMaterials.NormalBillboard);
            };

            MakeSurfaces();
        }

        private void MakeSurfaces () {
            // int w = Width, h = Height;
            int w = InputLeft.Width, h = InputLeft.Height;
            var format = HighPrecisionNormals ? SurfaceFormat.Vector4 : SurfaceFormat.Color;
            if ((GeneratedMap == null) || (GeneratedMap.Width != w) || (GeneratedMap.Height != h)) {
                Game.RenderCoordinator.DisposeResource(GeneratedMap);
                // GeneratedMap = new AutoRenderTarget(Game.RenderCoordinator, w, h, false, format);
                GeneratedMap = new Texture2D(Game.GraphicsDevice, w, h, false, SurfaceFormat.Vector4);
            }
            Lightmap = new RenderTarget2D(
                Game.GraphicsDevice, w, h, false,
                SurfaceFormat.Color, DepthFormat.None, 0, 
                RenderTargetUsage.PlatformContents
            );
        }

        private float[] AwfulReadback (Texture2D source) {
            var readbackBuf = new Color[source.Width * source.Height];
            source.GetData(readbackBuf);
            var resultBuf = new float[source.Width * source.Height];
            for (int i = 0; i < readbackBuf.Length; i++) {
                var value = readbackBuf[i].R;
                resultBuf[i] = (value / 255.0f);
            }
            return resultBuf;
        }

        private void GenerateNormalsOnCPU (float[][] inputs, int inputCount) {
            float z = ZBasis;

            for (int y = 0; y < GeneratedMap.Height; y++) {
                int rowIndex = (y * GeneratedMap.Width);
                for (int x = 0; x < GeneratedMap.Width; x++) {
                    int index = rowIndex + x;
                    float left = inputs[0][index],
                        right = inputCount > 1 ? inputs[1][index] : 1f - left,
                        above = inputCount > 2 ? inputs[2][index] : 0.5f,
                        below = inputCount > 3 ? inputs[3][index] : 1f- above;

                    Vector4 a = new Vector4((1f - left) * 0.5f, (1f - above) * 0.5f, 0.5f, 1f),
                        b = new Vector4(right * 0.5f + 0.5f, below * 0.5f + 0.5f, 0f, 1f),
                        n = new Vector4(
                            a.X <= 0.5f ? b.X * a.X * 2f : 1f - 2f * (1f - a.X) * (1f - b.X),
                            a.Y <= 0.5f ? b.Y * a.Y * 2f : 1f - 2f * (1f - a.Y) * (1f - b.Y),
                            0, 0f
                        );
                    n -= new Vector4(0.5f);
                    n *= 2.0f;
                    n.Z = z;
                    n.Normalize();

                    n *= 0.5f;
                    n += new Vector4(0.5f);

                    n.W = 1f;
                    OutputBuffer[index] = n;
                }
            }

            GeneratedMap.SetData(OutputBuffer);
        }

        public override void UnloadContent () {
        }

        public override void Draw (Frame frame) {
            var now = (float)Time.Seconds;

            MakeSurfaces();

            GenerateNormalsOnCPU(InputBuffers, (int)NumberOfInputs.Value);

            /*

            using (var gm = BatchGroup.ForRenderTarget(frame, -3, GeneratedMap)) {
                var ir = new ImperativeRenderer(gm, Game.Materials, blendState: BlendState.NonPremultiplied);
                ir.Clear(layer: 0, value: HighPrecisionNormals ? Vector4.Zero : new Vector4(0.5f, 0.5f, 0.5f, 0f));

                var tex1 = SpriteHeight;
                Material m = IlluminantMaterials.HeightmapToNormals;

                if (tex1 != null)
                    ir.Draw(tex1, Vector2.Zero, scale: SpriteSize.Value * Vector2.One, material: m);
            }
            */

            Renderer.UpdateFields(frame, -2);

            using (var lm = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                (dm, _) => {
                    Game.Materials.PushViewTransform(ViewTransform.CreateOrthographic(
                        Lightmap.Width, Lightmap.Height
                    ));
                },
                (dm, _) => {
                    Game.Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(lm, 0, Game.Materials.Clear, clearColor: Color.Black);

                var lighting = Renderer.RenderLighting(lm, 1, 1.0f);
                lighting.Resolve(
                    lm, 2, Lightmap.Width, Lightmap.Height,
                    hdr: new HDRConfiguration {
                        InverseScaleFactor = 1.0f,
                        Gamma = 1.0f,
                    }
                );
            };

            using (var fg = BatchGroup.New(frame, 1)) {
                var ir = new ImperativeRenderer(fg, Game.Materials, blendState: BlendState.NonPremultiplied);
                ir.Clear(layer: 0, color: new Color(0, 63, 127));
                // ir.Parameters.Add("TapSpacingAndBias", new Vector3(1.0f / HeightMap.Width * TapSpacing.Value, 1.0f / HeightMap.Height * TapSpacing.Value, MipBias));
                ir.Parameters.Add("DisplacementScale", Vector2.One);
                // ir.Parameters.Add("FieldIntensity", new Vector3(DisplacementScale.Value, DisplacementScale.Value, DisplacementScale.Value));
                // ir.Parameters.Add("RefractionIndexAndMipBias", new Vector2(RefractionIndex.Value, RefractionMipBias.Value));
                ir.Parameters.Add("NormalsAreSigned", HighPrecisionNormals);

                Material m = null;
                AbstractTextureReference tex1 = default,
                    tex2 = default;
                var bs = BlendState.Opaque;
                switch (ViewMode.Value) {
                    case "Left":
                        tex1 = InputLeft;
                        break;
                    case "Right":
                        tex1 = InputRight;
                        break;
                    case "Up":
                        tex1 = InputAbove;
                        break;
                    case "Down":
                        tex1 = InputBelow;
                        break;
                    case "Normals":
                        tex1 = new AbstractTextureReference(GeneratedMap);
                        break;
                    case "Lightmap":
                        tex1 = new AbstractTextureReference(Lightmap);
                        break;
                    case "GBuffer":
                        tex1 = new AbstractTextureReference(Renderer.GBuffer.Texture);
                        break;
                }

                var ts = new TextureSet(tex1, tex2);
                var dc = new BitmapDrawCall(
                    ts, Vector2.Zero, Bounds.Unit, Color.White, Vector2.One * 3f, Vector2.Zero, 0f
                ) {
                    UserData = Vector4.One
                };
                // dc.AlignTexture2(1.0f / SpriteSize.Value, true);
                ir.Draw(dc, material: m, blendState: bs);

                ir.Layer += 1;
            }
        }

        private void ScaleViewTransform (ref ViewTransform vt, object userData) {
            vt.Scale *= 0.5f;
        }

        public override void UIScene (ref ContainerBuilder builder) {
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                if (!Game.IsMouseOverUI) {
                }

                var pos = new Vector3(LightPosX.Value, LightPosY.Value, LightPosZ.Value);
                Environment.Lights.OfType<SphereLightSource>().First()
                    .Position = pos;

                Game.IsMouseVisible = true;
            }
        }
    }
}
