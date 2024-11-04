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
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class BitmapBillboards : Scene {
        DistanceField DistanceField;
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        RenderTarget2D Lightmap;

        public SphereLightSource MovableLight;

        Texture2D Background;
        float LightZ;

        const float LightScaleFactor = 4;

        [Group("Visualization")]
        Toggle ShowGBuffer,
            ShowDistanceField,
            ShowLightmap,
            ShowBillboards,
            UnlitBillboards;

        [Group("Lighting")]
        Slider MaximumLightStrength,
            LightSize;

        [Group("Resolution")]
        Slider DistanceFieldResolution,
            LightmapScaleRatio;

        Toggle Deterministic, EnableTreeShadows;
        Slider TreeNormal;

        Texture2D Tree;

        public BitmapBillboards (TestGame game, int width, int height)
            : base(game, 1024, 1024) {

            Deterministic.Value = true;
            DistanceFieldResolution.Value = 0.25f;
            LightmapScaleRatio.Value = 1.0f;
            MaximumLightStrength.Value = 2f;
            ShowLightmap.Value = false;

            ShowLightmap.Key = Keys.L;
            ShowGBuffer.Key = Keys.G;
            ShowDistanceField.Key = Keys.D;
            Deterministic.Key = Keys.R;
            ShowBillboards.Key = Keys.B;
            EnableTreeShadows.Key = Keys.S;

            DistanceFieldResolution.MinusKey = Keys.D5;
            DistanceFieldResolution.PlusKey = Keys.D6;
            DistanceFieldResolution.Min = 0.1f;
            DistanceFieldResolution.Max = 1.0f;
            DistanceFieldResolution.Speed = 0.05f;

            LightmapScaleRatio.MinusKey = Keys.D7;
            LightmapScaleRatio.PlusKey = Keys.D8;
            LightmapScaleRatio.Min = 0.05f;
            LightmapScaleRatio.Max = 1.0f;
            LightmapScaleRatio.Speed = 0.1f;
            LightmapScaleRatio.Changed += (s, e) => Renderer.InvalidateFields();

            MaximumLightStrength.Min = 1.0f;
            MaximumLightStrength.Max = 6.0f;
            MaximumLightStrength.Speed = 0.1f;

            LightSize.Value = 48;
            LightSize.Max = 1024;
            LightSize.Min = 4;

            DistanceFieldResolution.Changed += (s, e) => CreateDistanceField();

            TreeNormal.Min = -1;
            TreeNormal.Max = 1;
            TreeNormal.Value = 0;
            TreeNormal.Speed = 0.02f;
        }

        private void InitUnitSlider (params Slider[] sliders) {
            foreach (var s in sliders) {
                s.Max = 1.0f;
                s.Min = 0.0f;
                s.Speed = 0.02f;
            }
        }

        private void CreateRenderTargets () {
            if (Lightmap == null) {
                if (Lightmap != null)
                    Lightmap.Dispose();

                Lightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        HeightVolumeBase Rect (Vector2 a, Vector2 b, float z1, float height) {
            var result = new SimpleHeightVolume(
                Polygon.FromBounds(new Bounds(a, b)), z1, height 
            );
            Environment.HeightVolumes.Add(result);
            return result;
        }

        private void CreateDistanceField () {
            if (DistanceField != null) {
                Game.RenderCoordinator.DisposeResource(DistanceField);
                DistanceField = null;
            }

            DistanceField = new DistanceField(
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
            Environment.MaximumZ = 128;
            Environment.ZToYMultiplier = 2.5f;
            
            Background = Game.TextureLoader.Load("sc3test");

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment,
                new RendererConfiguration(
                    1024, 1024, true, enableBrightnessEstimation: false, stencilCulling: true
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

            Renderer.OnRenderGBuffer += (LightingRenderer lr, ref ImperativeRenderer ir) => {
                DrawTrees(ref ir);
            };

            CreateDistanceField();

            MovableLight = new SphereLightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Radius = 48,
                RampLength = 4,
                RampMode = LightSourceRampMode.Linear
            };

            Environment.Lights.Add(MovableLight);

            Environment.Lights.Add(new DirectionalLightSource {
                Color = new Vector4(0.2f, 0.2f, 0.2f, 1f),
                BlendMode = RenderStates.AdditiveBlend,
                SortKey = 1
            });

            Rect(new Vector2(330, 337), new Vector2(Width, 394), 0f, 55f);

            Tree = Game.TextureLoader.Load("tree1");
        }

        public override void UnloadContent () {
            Renderer?.Dispose(); Renderer = null;
        }

        private void DrawTrees (ref ImperativeRenderer ir) {
            var zMultiplier = 1.0f / Environment.ZToYMultiplier;
            var z = 10;

            var w = 
                UnlitBillboards
                    ? -1
                    : (EnableTreeShadows ? 1 : 0);

            for (int x = 400; x < 1100; x += 80) {
                ir.Draw(
                    Tree, new Vector2(x + 10, 250), userData: new Vector4(TreeNormal, zMultiplier, z, w),
                    origin: new Vector2(0, 1)
                );
                ir.Draw(
                    Tree, new Vector2(x, 500), sortKey: 30, userData: new Vector4(TreeNormal, zMultiplier, z, w),
                    origin: new Vector2(0, 1)
                );
            }
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            MovableLight.Radius = LightSize;

            Renderer.Configuration.TwoPointFiveD = true;
            Renderer.Configuration.SetScale(LightmapScaleRatio);

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
                        Gamma = 1.0f,
                    }
                );
            };

            using (var group = BatchGroup.New(frame, 0)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                if (ShowLightmap) {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(Game.Materials.Bitmap, blendState: BlendState.Opaque),
                        samplerState: SamplerState.LinearClamp
                    ))
                        bb.Add(new BitmapDrawCall(Lightmap, Vector2.Zero));
                } else {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(
                            ShowGBuffer
                                ? Game.Materials.Bitmap
                                : Game.Materials.LightmappedBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    )) {
                        var dc = new BitmapDrawCall(
                            Background, Vector2.Zero, Color.White * (ShowGBuffer ? 0.7f : 1.0f)
                        );
                        dc.Textures = new TextureSet(dc.Textures.Texture1, Lightmap);
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

                if (ShowGBuffer && Renderer.Configuration.EnableGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 4, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            Renderer.GBuffer.Texture.Get(), Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, LightmapScaleRatio
                        ));
                }

                if (ShowBillboards) {
                    var ir = new ImperativeRenderer(group, Game.Materials, 10);
                    DrawTrees(ref ir);
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;
                
                var time = (float)Time.Seconds;

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                LightZ = (ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ;

                if (LightZ < 0.01f)
                    LightZ = 0.01f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                if (Deterministic) {
                    MovableLight.Position = new Vector3(740, 540, 130f);
                    MovableLight.Color.W = 0.5f;
                } else {
                    MovableLight.Position = mousePos;
                    MovableLight.Color.W = Arithmetic.Pulse((float)Time.Seconds / 3f, 0.3f, MaximumLightStrength);
                }
            }
        }
    }
}
