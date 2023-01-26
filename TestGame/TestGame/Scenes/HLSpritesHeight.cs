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
    public class HLSpritesHeight : Scene {
        [Items("Albedo")]
        [Items("Height")]
        [Items("Distance")]
        [Items("Normals")]
        [Items("Lightmap")]
        [Items("Composited")]
        [Items("GBuffer")]
        Dropdown<string> ViewMode;

        [Group("Generator Settings")]
        Slider TapSpacing, MipBias;
        [Group("Generator Settings")]
        Toggle HighPrecisionNormals, ElevationClamping;

        [Group("Sprite Settings")]
        Slider HeightScale, BlurSigma, BlurSampleRadius, BlurMeanFactor, SpriteSize, SpriteBias, SpriteMasking;
        [Group("Sprite Settings")]
        Toggle UseMips;
        [Group("Light Settings")]
        Slider LightPosX, LightPosY, LightPosZ;

        private bool NeedToGenerateSDF = true;
        private Texture2D Background, SpriteAlbedo, SpriteHeight;
        private RenderTarget2D SpriteDistanceField;
        private AutoRenderTarget GeneratedMap;
        private IlluminantMaterials IlluminantMaterials;

        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;
        float LightZ;

        public HLSpritesHeight (TestGame game, int width, int height)
            : base(game, width, height) {
            ViewMode.Value = "Albedo";
            TapSpacing.Min = 0.5f;
            TapSpacing.Max = 16f;
            TapSpacing.Speed = 1f;
            TapSpacing.Value = 1f;
            MipBias.Min = -1f;
            MipBias.Max = 8f;
            MipBias.Speed = 0.5f;
            MipBias.Value = 0f;
            HighPrecisionNormals.Value = true;
            HeightScale.Min = 0.1f;
            HeightScale.Max = 2.0f;
            HeightScale.Value = 1.0f;
            HeightScale.Speed = 0.05f;
            BlurSigma.Min = 0.1f;
            BlurSigma.Max = 10.0f;
            BlurSigma.Value = 2f;
            BlurSigma.Speed = 0.05f;
            BlurSampleRadius.Integral = true;
            BlurSampleRadius.Min = 1;
            BlurSampleRadius.Max = 9;
            BlurSampleRadius.Value = 3;
            BlurSampleRadius.Speed = 1;
            BlurMeanFactor.Min = 0f;
            BlurMeanFactor.Max = 1f;
            BlurMeanFactor.Value = 0f;
            BlurMeanFactor.Speed = 0.05f;
            SpriteSize.Min = 0.05f;
            SpriteSize.Max = 2.0f;
            SpriteSize.Value = 1.0f;
            SpriteSize.Speed = 0.05f;
            SpriteBias.Min = -1f;
            SpriteBias.Max = 7f;
            SpriteBias.Value = 0f;
            SpriteBias.Speed = 0.25f;
            SpriteMasking.Min = 0f;
            SpriteMasking.Max = 1f;
            SpriteMasking.Value = 0.5f;
            SpriteMasking.Speed = 0.05f;
            LightPosX.Min = LightPosY.Min = LightPosZ.Min = -512f;
            LightPosX.Max = Width;
            LightPosY.Max = Height;
            LightPosZ.Max = 512f;
            LightPosX.Speed = LightPosY.Speed = LightPosZ.Speed = 16f;
            UseMips.Value = true;
        }

        public override void LoadContent () {
            Background = Game.TextureLoader.Load("vector-field-background");
            SpriteAlbedo = Game.TextureLoader.LoadSync("red-albedo", new TextureLoadOptions {
                // GenerateDistanceField = true,
                GenerateMips = true,
            }, true, false);
            SpriteHeight = Game.TextureLoader.LoadSync("red-heightmap", new TextureLoadOptions {
                GenerateMips = true,
            }, true, false);
            SpriteDistanceField = new RenderTarget2D(Game.GraphicsDevice, SpriteAlbedo.Width, SpriteAlbedo.Height, true, SurfaceFormat.Single, DepthFormat.None);

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
            int w = Width, h = Height;
            var format = HighPrecisionNormals ? SurfaceFormat.Vector4 : SurfaceFormat.Color;
            if ((GeneratedMap == null) || (GeneratedMap.Width != w) || (GeneratedMap.Height != h)) {
                Game.RenderCoordinator.DisposeResource(GeneratedMap);
                GeneratedMap = new AutoRenderTarget(Game.RenderCoordinator, w, h, false, format);
            }
            Lightmap = new RenderTarget2D(
                Game.GraphicsDevice, w, h, false,
                SurfaceFormat.Color, DepthFormat.None, 0, 
                RenderTargetUsage.PlatformContents
            );
        }

        public override void UnloadContent () {
        }

        public override void Draw (Frame frame) {
            var now = (float)Time.Seconds;

            MakeSurfaces();

            if (NeedToGenerateSDF) {
                var ir = new ImperativeRenderer(frame, Game.Materials);
                Squared.Render.DistanceField.JumpFlood.GenerateDistanceField(ref ir, SpriteAlbedo, SpriteDistanceField, layer: -4);
                NeedToGenerateSDF = false;
            }

            using (var gm = BatchGroup.ForRenderTarget(frame, -3, GeneratedMap)) {
                var ir = new ImperativeRenderer(gm, Game.Materials, blendState: BlendState.NonPremultiplied);
                ir.Clear(layer: 0, value: HighPrecisionNormals ? Vector4.Zero : new Vector4(0.5f, 0.5f, 0.5f, 0f));
                ir.Parameters.Add("TapSpacingAndBias", new Vector3(1.0f / SpriteHeight.Width * TapSpacing.Value, 1.0f / SpriteHeight.Height * TapSpacing.Value, MipBias));
                ir.Parameters.Add("DisplacementScale", Vector2.One);
                ir.Parameters.Add("NormalsAreSigned", HighPrecisionNormals);
                ir.Parameters.Add("NormalElevationClamping", ElevationClamping);

                var tex1 = SpriteHeight;
                Material m = IlluminantMaterials.HeightmapToNormals;

                if (tex1 != null)
                    ir.Draw(tex1, Vector2.Zero, scale: SpriteSize.Value * Vector2.One, material: m);
            }

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
                var scale = SpriteSize.Value;
                var bs = BlendState.Opaque;
                switch (ViewMode.Value) {
                    case "Albedo":
                        tex1 = new AbstractTextureReference(SpriteAlbedo);
                        break;
                    case "Height":
                        tex1 = new AbstractTextureReference(SpriteHeight);
                        break;
                    case "Distance":
                        tex1 = new AbstractTextureReference(SpriteDistanceField);
                        break;
                    case "Normals":
                        tex1 = new AbstractTextureReference(GeneratedMap);
                        scale = 1f;
                        break;
                    case "Lightmap":
                        tex1 = new AbstractTextureReference(Lightmap);
                        scale = 1f;
                        break;
                    case "GBuffer":
                        tex1 = new AbstractTextureReference(Renderer.GBuffer.Texture);
                        scale = 1f;
                        break;
                    case "Composited":
                    default:
                        // ir.Draw(Background, Vector2.Zero);
                        ir.Layer += 1;
                        tex1 = new AbstractTextureReference(SpriteAlbedo);
                        tex2 = new AbstractTextureReference(Lightmap);
                        m = Game.Materials.ScreenSpaceLightmappedBitmap;
                        bs = BlendState.NonPremultiplied;
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
