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
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class ProjectorLight : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public ProjectorLightSource MovableLight;

        const float LightScaleFactor = 1;

        Toggle ShowGBuffer,
            ShowDistanceField,
            Deterministic,
            Shadows, 
            Wrap;

        Slider Scale, Rotation, Depth, Elevation;
        Slider DistanceFieldResolution;

        const float MaximumZ = 128;

        public ProjectorLight (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            Deterministic.Value = true;
            DistanceFieldResolution.Value = 0.5f;
            Shadows.Value = true;

            ShowGBuffer.Key = Keys.G;
            ShowDistanceField.Key = Keys.D;
            Deterministic.Key = Keys.R;
            Shadows.Key = Keys.S;
            Wrap.Key = Keys.W;

            DistanceFieldResolution.MinusKey = Keys.D5;
            DistanceFieldResolution.PlusKey = Keys.D6;
            DistanceFieldResolution.Min = 0.1f;
            DistanceFieldResolution.Max = 1.0f;
            DistanceFieldResolution.Speed = 0.05f;

            Scale.MinusKey = Keys.OemSemicolon;
            Scale.PlusKey = Keys.OemQuotes;
            Scale.Min = 0.05f;
            Scale.Max = 4f;
            Scale.Speed = 0.05f;
            Scale.Value = 0.4f;

            Rotation.MinusKey = Keys.T;
            Rotation.PlusKey = Keys.Y;
            Rotation.Min = -3600;
            Rotation.Max = 3600;
            Rotation.Speed = 2;
            Rotation.Integral = false;

            Elevation.Min = -MaximumZ;
            Elevation.Max = MaximumZ;
            Elevation.Speed = 2;
            Elevation.MinusKey = Keys.D8;
            Elevation.PlusKey = Keys.D9;

            Depth.Min = 1;
            Depth.Max = MaximumZ * 2;
            Depth.Value = MaximumZ;
            Depth.Speed = 2;

            DistanceFieldResolution.Changed += (s, e) => CreateDistanceField();
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, 1, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        HeightVolumeBase Rect (Vector2 a, Vector2 b, float z1, float height) {
            var result = new SimpleHeightVolume(
                Polygon.FromBounds(new Bounds(a, b)), z1, height 
            ) { IsDynamic = false };
            Environment.HeightVolumes.Add(result);
            return result;
        }

        void Ellipse (Vector2 center, float radiusX, float radiusY, float z1, float height) {
            var numPoints = Math.Max(
                16,
                (int)Math.Ceiling((radiusX + radiusY) * 0.5f)
            );

            var pts = new Vector2[numPoints];
            float radiusStep = (float)((Math.PI * 2) / numPoints);
            float r = 0;

            for (var i = 0; i < numPoints; i++, r += radiusStep)
                pts[i] = new Vector2((float)Math.Cos(r) * radiusX, (float)Math.Sin(r) * radiusY) + center;
            
            var result = new SimpleHeightVolume(
                new Polygon(pts),
                z1, height
            ) { IsDynamic = false };
            Environment.HeightVolumes.Add(result);
        }

        void Pillar (Vector2 center) {
            const float totalHeight = 0.5f;

            var scale = 0.25f;
            var baseSizeTL = new Vector2(62, 65) * scale;
            var baseSizeBR = new Vector2(64, 57) * scale;
            Ellipse(center, 51f * scale, 45f * scale, 0, totalHeight * Environment.MaximumZ);
        }

        private void CreateDistanceField () {
            if (DistanceField != null) {
                Game.RenderCoordinator.DisposeResource(DistanceField);
                DistanceField = null;
            }

            DistanceField = new DynamicDistanceField(
                Game.RenderCoordinator, 1024, 1024, Environment.MaximumZ,
                64, DistanceFieldResolution.Value
            );
            if (Renderer != null) {
                Renderer.DistanceField = DistanceField;
                Renderer.InvalidateFields();
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Environment.GroundZ = 0;
            Environment.MaximumZ = MaximumZ;
            Environment.ZToYMultiplier = 1.5f;

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    1024, 1024, true
                ) {
                    MaximumFieldUpdatesPerFrame = 9,
                    DefaultQuality = {
                        MinStepSize = 1f,
                        LongStepFactor = 0.5f,
                        OcclusionToOpacityPower = 0.7f,
                        MaxConeRadius = 24,
                    },
                    EnableGBuffer = true,
                    TwoPointFiveD = true
                }, Game.IlluminantMaterials
            );

            CreateDistanceField();

            MovableLight = new ProjectorLightSource {
                Texture = (NullableLazyResource<Texture2D>)Game.TextureLoader.Load("test pattern")
            };

            Environment.Lights.Add(MovableLight);

            if (true) {
                Environment.Lights.Add(new DirectionalLightSource {
                    Direction = new Vector3(-0.75f, -0.7f, -0.33f),
                    Color = new Vector4(0.2f, 0.4f, 0.6f, 0.4f) * 0.7f,
                });

                Environment.Lights.Add(new DirectionalLightSource {
                    Direction = new Vector3(0.35f, -0.05f, -0.75f),
                    Color = new Vector4(0.5f, 0.3f, 0.15f, 0.3f) * 0.7f,
                });

                Environment.Ambient = new Color(32, 32, 32, 255);
            }

            Rect(new Vector2(330, 347), new Vector2(Width, 388), 0f, 55f);

            for (int x = -1024; x < (Width + 1024); x += 37)
                Pillar(new Vector2(x, 540 + (x / 24.0f)));
        }

        public override void UnloadContent () {
            DistanceField.Dispose();
            Renderer?.Dispose(); Renderer = null;
        }

        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            var scaleRatio = 1.0f;

            Renderer.Configuration.SetScale(scaleRatio);

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

                var lighting = Renderer.RenderLighting(bg, 1, 1.0f / LightScaleFactor);
                lighting.Resolve(
                    bg, 2, Width, Height, 
                    hdr: new HDRConfiguration {
                        InverseScaleFactor = LightScaleFactor,
                        Dithering = new DitheringSettings {
                            Power = 8,
                            Strength = 1
                        }
                    }
                );
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                if (ShowGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.AlphaBlend),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(Renderer.GBuffer.Texture.Get(), Vector2.Zero));
                } else {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.ScreenSpaceBitmap,
                        samplerState: SamplerState.PointClamp
                    )) {
                        var dc = new BitmapDrawCall(
                            Lightmap, Vector2.Zero, Color.White
                        );
                        bb.Add(dc);
                    }
                }

                if (ShowDistanceField) {
                    float dfScale = Math.Min(
                        (Game.Graphics.PreferredBackBufferWidth - 4) / (float)Renderer.DistanceField.Texture.Width,
                        (Game.Graphics.PreferredBackBufferHeight - 4) / (float)Renderer.DistanceField.Texture.Height
                    );

                    using (var bb = BitmapBatch.New(
                        group, 3, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.LinearClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.DistanceField.Texture.Get(), Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            new Color(255, 255, 255, 255), dfScale
                        ));
                }                
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;
                
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                MovableLight.CastsShadows = Shadows;

                var opacity = (ms.ScrollWheelValue / 4096.0f) + 0.2f;
                if (opacity > 4)
                    opacity = 4;
                if (opacity < -2)
                    opacity = -2;

                if (Deterministic && false) {
                } else {
                    MovableLight.Depth = Depth;
                    MovableLight.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.ToRadians(Rotation.Value));
                    MovableLight.Wrap = Wrap;
                    MovableLight.Scale = new Vector2(Scale);
                    MovableLight.BlendMode = (opacity < 0) ? RenderStates.SubtractiveBlend : RenderStates.AdditiveBlend;
                    MovableLight.Opacity = Math.Abs(opacity);
                    var tex = MovableLight.Texture.Instance;
                    int w = 656, h = 884;
                    MovableLight.TextureRegion = MovableLight.Texture.Instance.BoundsFromRectangle(new Rectangle(25, 36, w, h));
                    if (!Game.IsMouseOverUI)
                        MovableLight.Position = new Vector3(ms.X - w / 2 * Scale, ms.Y - h / 2 * Scale, Elevation);
                }
            }
        }
    }
}
