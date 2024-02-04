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
        [Items("Distance")]
        [Items("Left")]
        [Items("Right")]
        [Items("Up")]
        [Items("Down")]
        [Items("Normals")]
        [Items("Lightmap")]
        [Items("GBuffer")]
        [Items("Composited")]
        Dropdown<string> ViewMode;
        Slider SpriteSize;

        [Group("Generator Settings")]
        Toggle ShadowsOnly;
        [Group("Generator Settings")]
        Slider NumberOfInputs, ZMagnitude, MinInput, MaxInput, Inclination;

        [Group("GBuffer Settings")]
        Slider DistanceZMax, DistanceZScale;

        [Group("Light Settings")]
        Slider LightPosX, LightPosY, LightPosZ, LightSize, LightOcclusion,
            SpecularBrightness,
            SpecularPower;
        [Group("Light Settings")]
        Toggle Auto;

        bool HighPrecisionNormals = false;

        private Texture2D Albedo, InputLeft, InputRight, InputAbove, InputBelow;
        private Texture2D[] Inputs;
        private float[][] InputBuffers;
        private Vector4[] OutputBuffer;
        private AutoRenderTarget GeneratedMap;
        // private Texture2D GeneratedMap;
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
            MinInput.Value = 141f;
            MaxInput.Min = 1f;
            MaxInput.Max = 255f;
            MaxInput.Value = 200f;
            ZMagnitude.Min = 0.1f;
            ZMagnitude.Max = 2f;
            ZMagnitude.Value = 0.8f;
            Inclination.Min = -1.0f;
            Inclination.Max = 1.0f;
            Inclination.Value = 0f;
            LightPosX.Min = LightPosY.Min = LightPosZ.Min = -512f;
            LightPosX.Max = Width;
            LightPosY.Max = Height;
            LightPosZ.Max = 512f;
            LightPosX.Speed = LightPosY.Speed = LightPosZ.Speed = 16f;
            LightSize.Min = 32;
            LightSize.Max = 2048;
            LightSize.Value = 1024;
            LightSize.Speed = 64;
            LightOcclusion.Min = 0f;
            LightOcclusion.Max = 128f;
            LightOcclusion.Value = 24f;
            SpriteSize.Min = 0.1f;
            SpriteSize.Max = 3.0f;
            SpriteSize.Value = 0.9f;
            DistanceZMax.Min = 1;
            DistanceZMax.Max = 128;
            DistanceZMax.Value = 16;
            DistanceZScale.Min = -4f;
            DistanceZScale.Max = 4f;
            DistanceZScale.Value = -1f;
            ShadowsOnly.Value = true;
            Auto.Value = true;
            SpecularBrightness.Min = 0f;
            SpecularBrightness.Max = 2f;
            SpecularBrightness.Value = 0f;
            SpecularPower.Min = 0.05f;
            SpecularPower.Max = 64f;
            SpecularPower.Value = 16f;
        }

        public override void LoadContent () {
            Albedo = Game.TextureLoader.LoadSync("red-albedo", new TextureLoadOptions {
                GenerateDistanceField = true
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
            Environment.AmbientColor = new Color(63, 63, 63, 0);
            Environment.Lights.Add(new SphereLightSource {
                CastsShadows = false, Color = new Vector4(0.5f, 0.1f, 0.2f, 1f),
                Radius = 1f,
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
                    RenderGroundPlane = false,
                    EnableGBuffer = true,
                    LightOcclusion = 32f,
                }, Game.IlluminantMaterials
            );
            IlluminantMaterials = Renderer.IlluminantMaterials;

            Renderer.OnRenderGBuffer += (LightingRenderer lr, ref ImperativeRenderer ir) => {
                var texSet = new TextureSet(
                    GeneratedMap.Get(), Game.TextureLoader.GetDistanceField(Albedo)
                );
                ir.Parameters.Add("NormalsAreSigned", HighPrecisionNormals);
                ir.Parameters.Add("ZFromDistance", new Vector4(0, DistanceZMax, DistanceZScale, 0));
                ir.Draw(texSet, Vector2.Zero, material: IlluminantMaterials.NormalBillboard, scale: SpriteSize.Value * Vector2.One);
            };

            MakeSurfaces();
            NeedGenerate = true;
            EventHandler<float> eh = (s, e) => NeedGenerate = true;
            NumberOfInputs.Changed += eh;
            MinInput.Changed += eh;
            MaxInput.Changed += eh;
            ZMagnitude.Changed += eh;
            Inclination.Changed += eh;
            ShadowsOnly.Changed += (s, e) => NeedGenerate = true;
        }

        private void MakeSurfaces () {
            // int w = Width, h = Height;
            int w = InputLeft.Width, h = InputLeft.Height;
            var format = HighPrecisionNormals ? SurfaceFormat.Vector4 : SurfaceFormat.Color;
            if ((GeneratedMap == null) || (GeneratedMap.Width != w) || (GeneratedMap.Height != h)) {
                Game.RenderCoordinator.DisposeResource(GeneratedMap);
                GeneratedMap = new AutoRenderTarget(Game.RenderCoordinator, w, h, false, format);
                // GeneratedMap = new Texture2D(Game.GraphicsDevice, w, h, false, SurfaceFormat.Vector4);
            }
            Game.RenderCoordinator.DisposeResource(Lightmap);
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
        private float CleanInput (float value, float min, float range, bool shadowsOnly) {
            var result = (value - min) / range;
            if (shadowsOnly)
                result -= 0.5f;
            return Arithmetic.Saturate(result);
        }

        /*
        private void GenerateNormalsOnCPU (float[][] inputs, int inputCount) {
            NeedGenerate = false;
            var so = ShadowsOnly.Value;
            float zMagnitude = ZMagnitude, min = MinInput.Value / 255f,
                range = (MaxInput.Value - MinInput.Value) / 255f,
                inclination = Inclination.Value;

            for (int y = 0; y < GeneratedMap.Height; y++) {
                int rowIndex = (y * GeneratedMap.Width);
                for (int x = 0; x < GeneratedMap.Width; x++) {
                    int index = rowIndex + x;
                    float left = CleanInput(inputs[0][index], min, range, so),
                        right = inputCount > 1 ? CleanInput(inputs[1][index], min, range, so) : 1 - left,
                        above = inputCount > 2 ? CleanInput(inputs[2][index], min, range, so) : 0,
                        below = inputCount > 3 ? CleanInput(inputs[3][index], min, range, so) : 1 - above;

                    float xDelta = right - left,
                        yDelta = below - above;
                    var xy = new Vector2(xDelta, yDelta);
                    float xyLength = xy.Length(),
                        forward = xyLength <= 0.01f
                            ? 1f
                            : (xyLength >= 0.98f
                                ? 0f
                                : (float)Math.Sqrt(1.0 - xyLength)
                            ) * zMagnitude;

                    var n = new Vector4(xDelta, yDelta, forward + inclination, 0f);
                    n.Normalize();
                    n *= 0.5f;
                    n += new Vector4(0.5f);
                    n.W = 1f;

                    OutputBuffer[index] = n;
                }
            }

            GeneratedMap.SetData(OutputBuffer);
        }
        */

        public override void UnloadContent () {
        }

        public override void Draw (Frame frame) {
            var now = (float)Time.Seconds;

            /*
            if (NeedGenerate)
                GenerateNormalsOnCPU(InputBuffers, (int)NumberOfInputs.Value);
            */

            using (var gm = BatchGroup.ForRenderTarget(
                frame, -3, GeneratedMap,
                viewTransform: ViewTransform.CreateOrthographic(
                    GeneratedMap.Width, GeneratedMap.Height
                ), materialSet: Game.Materials
            )) {
                var ir = new ImperativeRenderer(gm, Game.Materials, blendState: BlendState.Opaque);
                ir.Clear(layer: 0, value: HighPrecisionNormals ? Vector4.Zero : new Vector4(0.5f, 0.5f, 0.5f, 0f));

                var textures = new TextureSet(InputLeft, InputRight);
                Material m = IlluminantMaterials.NormalsFromLightmaps;
                ir.Parameters.Add("AboveTexture", InputAbove);
                ir.Parameters.Add("BelowTexture", InputBelow);
                ir.Parameters.Add("ShadowsOnly", ShadowsOnly.Value);
                ir.Parameters.Add("InputCount", (int)NumberOfInputs.Value);
                ir.Parameters.Add("Configuration1", new Vector4(MinInput / 255.0f, MaxInput / 255.0f, ZMagnitude, Inclination));

                var dc = new BitmapDrawCall(textures, Vector2.Zero);

                ir.Draw(ref dc, material: m);
            }

            Renderer.Configuration.LightOcclusion = LightOcclusion;
            Renderer.UpdateFields(frame, -2);

            using (var lm = BatchGroup.ForRenderTarget(
                frame, -1, Lightmap,
                viewTransform: ViewTransform.CreateOrthographic(
                    Width, Height
                ), materialSet: Game.Materials
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
                var pos = Vector2.Zero;
                switch (ViewMode.Value) {
                    case "Albedo":
                        tex1 = Albedo;
                        break;
                    case "Distance":
                        tex1 = Game.TextureLoader.GetDistanceField(Albedo);
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
                        bs = BlendState.Opaque;
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
                        m = Game.Materials.LightmappedBitmap;
                        pos = new Vector2(16, 16);
                        break;
                }

                var ts = new TextureSet(tex1, tex2);
                var dc = new BitmapDrawCall(
                    ts, pos, Bounds.Unit, Color.White, Vector2.One * scale, Vector2.Zero, 0f
                ) {
                    UserData = Vector4.One
                };
                dc.AlignTexture2(1.0f / SpriteSize.Value, true);
                ir.Draw(dc, material: m, blendState: bs, layer: 16);

                var p = Environment.Lights.OfType<SphereLightSource>().First();
                ir.RasterizeEllipse(
                    new Vector2(p.Position.X, p.Position.Y), new Vector2(Arithmetic.Saturate(p.Position.Z / 16f) + 1.5f), Color.Yellow,
                    layer: p.Position.Z < 0 ? 15 : 17
                );

                ir.Layer = 18;
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

                if (Auto) {
                    var spriteSize = new Vector2(InputLeft.Width, InputLeft.Height) * SpriteSize;
                    var spriteCenter = new Vector2(16, 16) + (spriteSize * 0.5f);
                    double a1 = Time.Seconds / 0.9,
                        a2 = Time.Seconds / 6.33;
                    LightPosX.Value = (float)(Math.Cos(a1) * spriteSize.X * 0.5) + spriteCenter.X;
                    LightPosY.Value = (float)(Math.Sin(a2) * spriteSize.Y * 0.5) + spriteCenter.Y;
                    LightPosZ.Value = (float)(Math.Sin(a1) * 24);
                }
                var pos = new Vector3(LightPosX.Value, LightPosY.Value, LightPosZ.Value);
                var l = Environment.Lights.OfType<SphereLightSource>().First();
                l.RampLength = LightSize.Value;
                l.Position = pos;
                l.SpecularColor = new Vector3(SpecularBrightness);
                l.SpecularPower = SpecularPower;

                Game.IsMouseVisible = true;
            }
        }
    }
}
