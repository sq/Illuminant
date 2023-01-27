using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
        [Items("Albedo")]
        [Items("Left")]
        [Items("Right")]
        [Items("Up")]
        [Items("Down")]
        [Items("Normals")]
        [Items("Lightmap")]
        [Items("GBuffer")]
        [Items("Composited")]
        Dropdown<string> ViewMode;

        [Group("Generator Settings")]
        // Toggle HighPrecisionNormals;
        bool HighPrecisionNormals = false;
        [Group("Generator Settings")]
        Slider NumberOfInputs, ZBasis, MinInput, MaxInput;

        [Group("Light Settings")]
        Slider LightPosX, LightPosY, LightPosZ;

        Slider SpriteSize;

        private Texture2D Albedo, InputLeft, InputRight, InputAbove, InputBelow;
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
        public bool NeedGenerate = true;

        public HLSpritesSolve (TestGame game, int width, int height)
            : base(game, width, height) {
            ViewMode.Value = "Normals";
            NumberOfInputs.Min = 1;
            NumberOfInputs.Max = 4;
            NumberOfInputs.Integral = true;
            NumberOfInputs.Value = 3;
            MinInput.Min = 0f;
            MinInput.Max = 254f;
            MinInput.Value = 139f;
            MaxInput.Min = 1f;
            MaxInput.Max = 255f;
            MaxInput.Value = 204f;
            ZBasis.Min = 0.1f;
            ZBasis.Max = 2f;
            ZBasis.Value = 0.5f;
            LightPosX.Min = LightPosY.Min = LightPosZ.Min = -512f;
            LightPosX.Max = Width;
            LightPosY.Max = Height;
            LightPosZ.Max = 512f;
            LightPosX.Speed = LightPosY.Speed = LightPosZ.Speed = 16f;
            SpriteSize.Min = 0.1f;
            SpriteSize.Max = 3.0f;
            SpriteSize.Value = 0.5f;
        }

        public override void LoadContent () {
            Albedo = Game.TextureLoader.LoadSync("normalgen-albedo", new TextureLoadOptions {
            }, true, false);
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
                Radius = 1f,
                RampLength = 1024f,
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
                ir.Draw(GeneratedMap, Vector2.Zero, material: IlluminantMaterials.NormalBillboard, scale: SpriteSize.Value * Vector2.One);
            };

            MakeSurfaces();
            NeedGenerate = true;
            EventHandler<float> eh = (s, e) => {
                NeedGenerate = true;
            };
            NumberOfInputs.Changed += eh;
            MinInput.Changed += eh;
            MaxInput.Changed += eh;
            ZBasis.Changed += eh;
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
                Game.GraphicsDevice, Width, Height, false,
                SurfaceFormat.Color, DepthFormat.None, 0, 
                RenderTargetUsage.PlatformContents
            );
        }

        private float[] AwfulReadback (Texture2D source) {
            var readbackBuf = new Color[source.Width * source.Height];
            source.GetData(readbackBuf);
            var resultBuf = new float[source.Width * source.Height];
            for (int i = 0; i < readbackBuf.Length; i++) {
                if (readbackBuf[i].A < 16)
                    resultBuf[i] = float.NaN;
                else
                    resultBuf[i] = readbackBuf[i].R / 255.0f;
            }
            return resultBuf;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float CleanInput (float value, float min, float range) {
            return Arithmetic.Saturate((value - min) / range);
        }

        private void GenerateNormalsOnCPU (float[][] inputs, int inputCount) {
            NeedGenerate = false;

            float z = ZBasis, min = MinInput.Value / 255f, range = (MaxInput.Value - MinInput.Value) / 255f;

            for (int y = 0; y < GeneratedMap.Height; y++) {
                int rowIndex = (y * GeneratedMap.Width);
                for (int x = 0; x < GeneratedMap.Width; x++) {
                    int index = rowIndex + x;
                    float left = CleanInput(inputs[0][index], min, range),
                        right = inputCount > 1 ? CleanInput(inputs[1][index], min, range) : 1f - left,
                        above = inputCount > 2 ? CleanInput(inputs[2][index], min, range) : 0.5f,
                        below = inputCount > 3 ? CleanInput(inputs[3][index], min, range) : 1f - above;

                    if (float.IsNaN(left) || float.IsNaN(right) || float.IsNaN(above) || float.IsNaN(below)) {
                        OutputBuffer[index] = default;
                        continue;
                    }

                    Vector4 n = new Vector4(-left, 0, 0, 0) +
                        new Vector4(right, 0, 0, 0) +
                        new Vector4(0, -above, 0, 0) +
                        new Vector4(0, below, 0, 0);
                    n *= 1f / 4f;
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

            if (NeedGenerate)
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
                        Width, Height
                    ));
                },
                (dm, _) => {
                    Game.Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(lm, 0, Game.Materials.Clear, clearColor: Color.Black);

                var lighting = Renderer.RenderLighting(lm, 1, 1.0f);
                lighting.Resolve(
                    lm, 2, Width, Height,
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
                float scale = SpriteSize.Value;
                switch (ViewMode.Value) {
                    case "Albedo":
                        tex1 = Albedo;
                        break;
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
                        bs = BlendState.NonPremultiplied;
                        break;
                    case "Lightmap":
                        tex1 = new AbstractTextureReference(Lightmap);
                        scale = 1;
                        break;
                    case "GBuffer":
                        tex1 = new AbstractTextureReference(Renderer.GBuffer.Texture);
                        scale = 1;
                        break;
                    case "Composited":
                        tex1 = new AbstractTextureReference(Albedo);
                        tex2 = new AbstractTextureReference(Lightmap);
                        m = Game.Materials.ScreenSpaceLightmappedBitmap;
                        break;
                }

                var ts = new TextureSet(tex1, tex2);
                var dc = new BitmapDrawCall(
                    ts, Vector2.Zero, Bounds.Unit, Color.White, Vector2.One * scale, Vector2.Zero, 0f
                ) {
                    UserData = Vector4.One
                };
                dc.AlignTexture2(1.0f / SpriteSize.Value, true);
                ir.Draw(dc, material: m, blendState: bs);

                ir.Layer += 1;

                var p = Environment.Lights.OfType<SphereLightSource>().First();
                ir.RasterizeEllipse(
                    new Vector2(p.Position.X, p.Position.Y), new Vector2(Arithmetic.Saturate(p.Position.Z / 32f) + 2f), Color.Yellow
                );

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
