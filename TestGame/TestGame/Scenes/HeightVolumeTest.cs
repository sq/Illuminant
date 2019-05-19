using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Illuminant;
using Squared.Render;
using Squared.Util;

namespace TestGame.Scenes {
    public class HeightVolumeTest : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public readonly List<SphereLightSource> Lights = new List<SphereLightSource>();

        Toggle ShowGBuffer, ShowDistanceField, TwoPointFiveD, Deterministic, Shadows;
        Slider LightmapScaleRatio, MaxStepCount, MinStepSize, DistanceFieldResolution, DistanceSliceCount, MaximumEncodedDistance;

        public const int RotatingLightCount = 1024;

        public const int MultisampleCount = 0;

        float LightZ = 0;

        public HeightVolumeTest (TestGame game, int width, int height)
            : base(game, width, height) {

            Deterministic.Value = true;
            TwoPointFiveD.Value = true;
            LightmapScaleRatio.Value = 0.5f;
            MaxStepCount.Value = 64;
            MinStepSize.Value = 2f;
            DistanceFieldResolution.Value = 0.5f;
            DistanceSliceCount.Value = 7;
            MaximumEncodedDistance.Value = 128;
            Shadows.Value = true;

            ShowGBuffer.Key = Keys.G;
            TwoPointFiveD.Key = Keys.D2;
            TwoPointFiveD.Changed += (s, e) => Renderer.InvalidateFields();
            Deterministic.Key = Keys.R;

            DistanceFieldResolution.Min = 0.1f;
            DistanceFieldResolution.Max = 1.0f;
            DistanceFieldResolution.Speed = 0.05f;
            DistanceFieldResolution.Changed += (s, e) => CreateDistanceField();

            DistanceSliceCount.Min = 3;
            DistanceSliceCount.Max = 24;
            DistanceSliceCount.Speed = 1;
            DistanceSliceCount.Changed += (s, e) => CreateDistanceField();
            DistanceSliceCount.Integral = true;

            LightmapScaleRatio.MinusKey = Keys.D7;
            LightmapScaleRatio.PlusKey = Keys.D8;
            LightmapScaleRatio.Min = 0.05f;
            LightmapScaleRatio.Max = 1.0f;
            LightmapScaleRatio.Speed = 0.1f;
            LightmapScaleRatio.Changed += (s, e) => Renderer.InvalidateFields();

            MaximumEncodedDistance.Min = 16;
            MaximumEncodedDistance.Max = 320;
            MaximumEncodedDistance.Speed = 8;
            MaximumEncodedDistance.Changed += (s, e) => CreateDistanceField();

            MaxStepCount.Max = 128;
            MaxStepCount.Min = 32;
            MaxStepCount.Speed = 1;
            MaxStepCount.Integral = true;

            MinStepSize.Min = 0.5f;
            MinStepSize.Max = 5f;
            MinStepSize.Speed = 0.1f;
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, MultisampleCount, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        private void CreateDistanceField () {
            if (DistanceField != null) {
                Game.RenderCoordinator.DisposeResource(DistanceField);
                DistanceField = null;
            }

            DistanceField = new DistanceField(
                Game.RenderCoordinator, Width, Height, Environment.MaximumZ,
                (int)DistanceSliceCount.Value, DistanceFieldResolution.Value, (int)MaximumEncodedDistance.Value
            );
            if (Renderer != null) {
                Renderer.DistanceField = DistanceField;
                Renderer.InvalidateFields();
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();
            Lights.Clear();

            Environment.GroundZ = 0;
            Environment.MaximumZ = 128;
            Environment.ZToYMultiplier = 1.25f;

            CreateDistanceField();

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment,
                new RendererConfiguration(
                    Width, Height, true
                ) {
                    RenderScale = Vector2.One * LightmapScaleRatio,
                    GBufferCaching = true,
                    DefaultQuality = {
                        LongStepFactor = 0.95f
                    }
                }, Game.IlluminantMaterials
            ) {
                DistanceField = DistanceField
            };

            var light = new SphereLightSource {
                Position = new Vector3(64, 64, 0),
                Color = new Vector4(1f, 1f, 1f, 1f),
                Opacity = 0.5f,
                Radius = 60,
                RampLength = 450,
                RampMode = LightSourceRampMode.Exponential
            };

            Lights.Add(light);
            Environment.Lights.Add(light);

            float opacityScale = Math.Min((float)Math.Pow(32.0 / RotatingLightCount, 1.1), 2);
            float radiusScale  = Arithmetic.Clamp(32.0f / RotatingLightCount, 0.75f, 1.5f);

            var rng = new Random(1234);
            for (var i = 0; i < RotatingLightCount; i++) {
                light = new SphereLightSource {
                    Position = new Vector3(64, 64, rng.NextFloat(0.1f, 2.0f)),
                    Color = new Vector4((float)rng.NextDouble(0.1f, 0.7f), (float)rng.NextDouble(0.1f, 0.7f), (float)rng.NextDouble(0.1f, 0.7f), 1f),
                    Opacity = opacityScale,
                    Radius = rng.NextFloat(36, 68) * radiusScale,
                    RampLength = rng.NextFloat(180, 300) * radiusScale,
                    RampMode = LightSourceRampMode.Exponential
                };

                Lights.Add(light);
                Environment.Lights.Add(light);
            }

            const float angleStep = (float)(Math.PI / 128);
            const int   heightTiers = 8;
            const float minHeight = 0f;
            const float maxHeight = 120f;

            var points = new List<Vector2>();

            for (float r = 0.9f, hs = (maxHeight - minHeight) / heightTiers, rs = -r / (heightTiers + 1), h = minHeight + hs; h <= maxHeight; h += hs, r += rs) {
                points.Clear();

                var rX = r * Width / 2f;
                var rY = r * Height / 2f;
                for (float a = 0, p2 = (float)(Math.PI * 2); a < p2; a += angleStep) {
                    points.Add(new Vector2(
                        ((float)Math.Cos(a) * rX) + (Width / 2f),
                        ((float)Math.Sin(a) * rY) + (Height / 2f)
                    ));
                }

                var volume = new SimpleHeightVolume(
                    new Polygon(points.ToArray()),
                    0.0f, h
                );

                Environment.HeightVolumes.Add(volume);
            }

            Environment.HeightVolumes.Add(new SimpleHeightVolume(
                Polygon.FromBounds(new Bounds(
                    new Vector2((Width * 0.5f) - 32f, 0f),
                    new Vector2((Width * 0.5f) + 32f, Height * 2)
                )),
                0f, maxHeight
            ));
        }

        public override void UnloadContent () {
            DistanceField.Dispose();
            Renderer?.Dispose(); Renderer = null;
        }

        public override void Draw (Squared.Render.Frame frame) {
            Renderer.Configuration.TwoPointFiveD = TwoPointFiveD;
            Renderer.Configuration.SetScale(LightmapScaleRatio);
            Renderer.Configuration.DefaultQuality.MinStepSize = MinStepSize;
            Renderer.Configuration.DefaultQuality.MaxStepCount = (int)MaxStepCount;

            CreateRenderTargets();

            Renderer.UpdateFields(frame, -2);

            using (var bg = BatchGroup.ForRenderTarget(
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
                ClearBatch.AddNew(bg, 0, Game.Materials.Clear, clearColor: Color.Black);

                var lighting = Renderer.RenderLighting(bg, 1, intensityScale: 1);
                float mult = 1.0f / LightmapScaleRatio;
                lighting.Resolve(bg, 2, Width * mult, Height * mult);
            };

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, clearColor: Color.Black);

            using (var bb = BitmapBatch.New(
                frame, 1,
                Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                samplerState: ShowGBuffer ? SamplerState.PointClamp : SamplerState.LinearClamp
            )) {
                var dc = new BitmapDrawCall(
                    ShowGBuffer
                        ? Renderer.GBuffer.Texture
                        : Lightmap, 
                    Vector2.Zero,
                    Color.White
                );

                bb.Add(ref dc);
            }

            if (ShowDistanceField)
            using (var bb = BitmapBatch.New(
                frame, 2,
                Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                samplerState: SamplerState.LinearClamp
            )) {
                var dc = new BitmapDrawCall(
                    DistanceField.Texture,
                    Vector2.Zero,
                    Color.White
                ) {
                    ScaleF = Width / (float)DistanceField.Texture.Width
                };

                bb.Add(ref dc);
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                // const float minZ = 0f, maxZ = 1.5f;
                // LightZ = Squared.Util.Arithmetic.PulseSine((float)gameTime.TotalGameTime.TotalSeconds * 0.66f, minZ, maxZ);
                LightZ = ms.ScrollWheelValue / 2048.0f * 128f;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;
                
                var mousePos = new Vector2(ms.X, ms.Y);

                var time = gameTime.TotalGameTime.TotalSeconds;
                if (Deterministic)
                    time = 3.3;

                var angle = time * 0.125f;
                const float minRadius = 30f;
                const float maxRadius = 650f;
                const float centerMinZ = 200;
                const float outsideMinZ = 40;

                var lightCenter = new Vector3(Width / 2, Height / 2, 0);

                if (Deterministic)
                    Lights[0].Position = new Vector3(350, 900, 170);
                else
                    Lights[0].Position = new Vector3(mousePos, LightZ);
                Lights[0].CastsShadows = Shadows;
                // Lights[0].RampEnd = 250f * (((1 - LightZ) * 0.25f) + 0.75f);

                int count = Environment.Lights.Count - 1;

                float stepOffset = (float)((Math.PI * 2) / count);
                float timeValue = (float)(time / 14 % 4);
                float offset = timeValue;
                for (int i = 1; i < Environment.Lights.Count; i++, offset += stepOffset) {
                    Lights[i].CastsShadows = Shadows;

                    float radiusFactor = (float)Math.Abs(Math.Sin(
                        (i / (float)count * 8.7f) + timeValue
                    ));
                    float localRadius = Arithmetic.Lerp(minRadius, maxRadius, radiusFactor);
                    float localZ = Arithmetic.Lerp(centerMinZ, outsideMinZ, radiusFactor) +
                        Arithmetic.Pulse(timeValue, 0, 40);

                    Lights[i].Position = lightCenter + new Vector3(
                        (float)Math.Cos(angle + offset) * localRadius, 
                        (float)Math.Sin(angle + offset) * localRadius,
                        localZ
                    );
                }
            }
        }
    }
}
