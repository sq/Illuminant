
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
using Squared.Render.Evil;
using Squared.Util;

namespace TestGame.Scenes {
    public class SC3 : Scene {
        private static Random RNG = new Random();

        public class Projectile {
            public const float TargetVelocity = 4.25f; // +1f
            public const int AccelerateDuration = 100;
            public const int SeekDuration = 220;
            public const int FadeDuration = 45;

            private static int _NextIndex;

            public readonly int Index;
            public readonly SphereLightSource Light;
            public readonly Vector3 Source, Target;
            public readonly float Size;

            public Vector3 Velocity;
            public int     Age;
            public int     Life = FadeDuration;

            public Projectile (Vector3 source, Vector3 target) {
                Index = _NextIndex++;

                Source = source;
                Target = target;

                Size = RNG.NextFloat(0.85f, 1.05f);

                Velocity = new Vector3(
                    RNG.NextFloat(-1, 1),
                    RNG.NextFloat(-1, 1),
                    RNG.NextFloat(-1, 1)
                );
                Velocity.Normalize();
                Velocity *= RNG.NextFloat(2.25f, 3.6f);

                var position = source + (Velocity * RNG.NextFloat(0.3f, 0.8f));

                Light = new SphereLightSource {
                    Position = position,
                    CastsShadows = true,
                    AmbientOcclusionRadius = 0,
                    Color = new Vector4(
                        RNG.NextFloat(0.5f, 1.0f),
                        RNG.NextFloat(0.5f, 1.0f),
                        RNG.NextFloat(0.5f, 1.0f),
                        0.1f * Size
                    ),
                    Opacity = 1,
                    Radius = 3f * Size,
                    RampLength = 160f * Size,
                    RampMode = LightSourceRampMode.Exponential
                };
            }

            public bool Update () {
                var nextPosition = Light.Position + Velocity;
                var targetVector = Target - nextPosition;
                var distance = targetVector.Length();
                targetVector /= distance;
                var targetVelocity = (TargetVelocity * Arithmetic.Clamp(Age / (float)AccelerateDuration, 0, 1)) + 1f;
                targetVector *= Math.Min(distance, targetVelocity);

                Velocity = Arithmetic.Lerp(Velocity, targetVector, Age / (float)SeekDuration);

                if (distance <= 1.5f) {
                    Velocity = Vector3.Zero;
                    Light.Position = Target;
                    Life -= 1;
                } else {
                    Light.Position = nextPosition;
                }

                Age += 1;

                float opacity = Arithmetic.Clamp(Life / (float)FadeDuration, 0f, 1f);
                Light.Opacity = (float)Math.Pow(opacity, 0.85);

                return Life > 0;
            }
        }

        DistanceField DistanceField;
        LightingEnvironment Environment, ForegroundEnvironment;
        LightingRenderer Renderer, ForegroundRenderer;

        RenderTarget2D Lightmap, ForegroundLightmap;

        public readonly List<LightSource> Lights = new List<LightSource>();
        public readonly UnorderedList<Projectile> Projectiles = new UnorderedList<Projectile>();
        public readonly Queue<float> ExposureSamples = new Queue<float>(ExposureSampleCount);

        Texture2D Background, Foreground, BackgroundMask;
        Texture2D[] Trees;
        Texture2D[] Pillars;
        Texture2D Spark;
        float LightZ;

        const int ExposureSampleCount = 40;

        const int BackgroundScaleRatio = 1;
        const int ForegroundScaleRatio = 1;
        // We scale down the range of lighting values by this much, so that we
        //  have additional values past 1.0 to use for HDR calculations
        const float HDRRangeFactor = 4;

        bool VisualizeForeground = false;
        bool ShowGBuffer         = false;
        bool ShowLightmap        = false;
        bool ShowDistanceField   = false;
        bool Deterministic       = true;

        public SC3 (TestGame game, int width, int height)
            : base(game, 1396, 768) {
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

            if (ForegroundLightmap == null) {
                if (ForegroundLightmap != null)
                    ForegroundLightmap.Dispose();

                ForegroundLightmap = new RenderTarget2D(
                    Game.GraphicsDevice, Width, Height, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, 
                    RenderTargetUsage.PlatformContents
                );
            }
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();
            ForegroundEnvironment = new LightingEnvironment();

            Environment.GroundZ = ForegroundEnvironment.GroundZ = 64;
            Environment.MaximumZ = ForegroundEnvironment.MaximumZ = 200;
            Environment.ZToYMultiplier = ForegroundEnvironment.ZToYMultiplier = 2.5f;

            Background = Game.Content.Load<Texture2D>("bg_noshadows");
            BackgroundMask = Game.Content.Load<Texture2D>("bg_mask");
            Foreground = Game.Content.Load<Texture2D>("fg");

            Trees = new[] {
                Game.Content.Load<Texture2D>("tree1"),
                Game.Content.Load<Texture2D>("tree2")
            };
            Pillars = new[] {
                Game.Content.Load<Texture2D>("pillar1"),
                Game.Content.Load<Texture2D>("pillar2"),
                Game.Content.Load<Texture2D>("pillar3"),
            };

            Spark = Game.Content.Load<Texture2D>("spark");

            DistanceField = new DistanceField(
                Game.RenderCoordinator, Width, Height, Environment.MaximumZ,
                16, 0.5f
            );

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment,
                new RendererConfiguration(
                    Width / BackgroundScaleRatio, Height / BackgroundScaleRatio, true, true
                ) {
                    RenderScale = new Vector2(1.0f / BackgroundScaleRatio),
                    DistanceFieldMinStepSize = 1.5f,
                    DistanceFieldLongStepFactor = 0.7f,
                    DistanceFieldOcclusionToOpacityPower = 1.35f,
                    DistanceFieldMaxConeRadius = 30,
                    TwoPointFiveD = true,
                    DistanceFieldUpdateRate = 1
                }
            ) {
                DistanceField = DistanceField
            };

            ForegroundRenderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, ForegroundEnvironment, 
                new RendererConfiguration(
                    Width / ForegroundScaleRatio, Height / ForegroundScaleRatio, true
                ) {
                    RenderScale = new Vector2(1.0f / ForegroundScaleRatio),
                    DistanceFieldMinStepSize = 1.5f,
                    DistanceFieldLongStepFactor = 0.7f,
                    DistanceFieldOcclusionToOpacityPower = 1.35f,
                    DistanceFieldMaxConeRadius = 30,
                    TwoPointFiveD = true,
                    DistanceFieldUpdateRate = 0,
                }
            ) {
                DistanceField = DistanceField
            };

            var light = new SphereLightSource {
                Position = new Vector3(64, 64, 0.7f),
                Color = new Vector4(1f, 1f, 1f, 0.5f),
                Radius = 160,
                RampLength = 360,
                RampMode = LightSourceRampMode.Exponential,
                AmbientOcclusionRadius = 8f,
                FalloffYFactor = 4f
            };

            Environment.Lights.Add(light);

            light = light.Clone();
            light.AmbientOcclusionRadius = 0;
            ForegroundEnvironment.Lights.Add(light);

            var ambientLight = new DirectionalLightSource {
                Direction = new Vector3(-0.7f, -0.7f, -1.66f),
                ShadowTraceLength = 72f,
                ShadowRampLength = 400f,
                ShadowRampRate = 0.15f,
                ShadowSoftness = 12f,
                Color = new Vector4(0.33f, 0.85f, 0.65f, 0.15f),
                CastsShadows = true,
            };

            Environment.Lights.Add(ambientLight);

            ForegroundEnvironment.Lights.Add(ambientLight.Clone());

            BuildObstacles();

            var terrainBillboard = new Billboard {
                Position = Vector3.Zero,
                Size = new Vector3(Width, Height, 0),
                Normal = Vector3.UnitY,
                Texture = BackgroundMask,
                DataScale = 60,
                Type = BillboardType.GBufferData
            };

            Environment.Billboards.Add(terrainBillboard);
            ForegroundEnvironment.Billboards.Add(terrainBillboard);

            for (int i = 0; i < Math.Min(4, ExposureSampleCount); i++)
                ExposureSamples.Enqueue(1.0f);
        }

        private void Pillar (float x, float y, int textureIndex) {
            var tex = Pillars[textureIndex];

            var obs = new LightObstruction(
                LightObstructionType.Cylinder,
                // HACK: Is it right to need a fudge factor here?
                new Vector3(x, y - 5, Environment.GroundZ),
                new Vector3(18, 8, tex.Height)
            );
            Environment.Obstructions.Add(obs);
            ForegroundEnvironment.Obstructions.Add(obs);

            ForegroundEnvironment.Billboards.Add(new Billboard {
                Position = new Vector3(x - (tex.Width * 0.5f), y - tex.Height + 12, 0),
                Normal = Vector3.UnitZ,
                Size = new Vector3(tex.Width, tex.Height, 0),
                Texture = tex,
                CylinderNormals = true
            });
        }

        private void Tree (float x, float y, int textureIndex) {
            var obs = new LightObstruction(
                LightObstructionType.Cylinder,
                new Vector3(x, y, Environment.GroundZ),
                new Vector3(12, 6, 65)
            );
            Environment.Obstructions.Add(obs);
            ForegroundEnvironment.Obstructions.Add(obs);

            var tex = Trees[textureIndex];
            ForegroundEnvironment.Billboards.Add(new Billboard {
                Position = new Vector3(x - (tex.Width * 0.5f) - 14, y - tex.Height + 20, 0),
                Normal = Vector3.UnitZ,
                Size = new Vector3(tex.Width, tex.Height, 0),
                Texture = tex,
                CylinderNormals = false
            });
        }

        private void BuildObstacles () {
            Pillar(722, 186, 2);
            Pillar(849, 209, 0);
            Pillar(927, 335, 0);
            Pillar(888, 482, 1);
            Pillar(723, 526, 2);
            Pillar(593, 505, 2);

            Tree(214, 357, 0);
            Tree(298, 399, 0);
            Tree(426, 421, 0);
            Tree(173, 505, 0);
            Tree(383, 609, 0);
            Tree(556, 314, 1);
            Tree(221, 526, 1);
        }

        const float TargetAverageBrightness = 0.33f;

        private void HandleEstimatedBrightness (LightmapInfo lightmapInfo) {
            // HACK: Ramp between the average and maximum based on the number of overexposed pixels
            float peakValue = Arithmetic.Lerp(
                lightmapInfo.Mean, lightmapInfo.Maximum, lightmapInfo.Overexposed * 2.75f
            );
            // Set an exposure to try and balance the scene brightness
            // TODO: Set a white point?
            lock (ExposureSamples)
            if (peakValue > 0) {
                float immediateExposure = Arithmetic.Clamp(
                    TargetAverageBrightness / peakValue,
                    0.33f, 1.33f
                );
                if (ExposureSamples.Count >= ExposureSampleCount)
                    ExposureSamples.Dequeue();

                ExposureSamples.Enqueue(immediateExposure);
            }
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            CreateRenderTargets();

            DistanceField.Invalidate();

            Renderer.UpdateFields(frame, -16);
            ForegroundRenderer.UpdateFields(frame, -16);

            Renderer.EstimateBrightness(
                HandleEstimatedBrightness,
                HDRRangeFactor, TargetAverageBrightness, 3
            );

            float exposure;
            lock (ExposureSamples)
                exposure = ExposureSamples.Average();

            var hdrConfiguration = new HDRConfiguration {
                Mode = HDRMode.ToneMap,
                InverseScaleFactor = HDRRangeFactor,
                ToneMapping = {
                    Exposure = exposure,
                    WhitePoint = 1.0f
                }
            };

            int layer = -8;
            Renderer.RenderLighting(frame, layer, 1.0f / HDRRangeFactor);
            ForegroundRenderer.RenderLighting(frame, layer++, 1.0f / HDRRangeFactor);

            using (var bg = BatchGroup.ForRenderTarget(
                frame, layer++, Lightmap,
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

                Renderer.ResolveLighting(
                    bg, 1, 
                    new BitmapDrawCall(Renderer.Lightmap, Vector2.Zero, new Vector2(BackgroundScaleRatio, BackgroundScaleRatio)),
                    hdrConfiguration
                );
            };

            using (var fg = BatchGroup.ForRenderTarget(
                frame, layer++, ForegroundLightmap,
                (dm, _) => {
                    Game.Materials.PushViewTransform(ViewTransform.CreateOrthographic(
                        Width, Height
                    ));
                },
                (dm, _) => {
                    Game.Materials.PopViewTransform();
                }
            )) {
                ClearBatch.AddNew(fg, 0, Game.Materials.Clear, clearColor: new Color(0.5f, 0.5f, 0.5f, 1f));

                ForegroundRenderer.ResolveLighting(
                    fg, 1, 
                    new BitmapDrawCall(ForegroundRenderer.Lightmap, Vector2.Zero, new Vector2(ForegroundScaleRatio, ForegroundScaleRatio)),
                    hdrConfiguration
                );
            };

            using (var group = BatchGroup.New(frame, layer++)) {
                ClearBatch.AddNew(group, 0, Game.Materials.Clear, clearColor: Color.Blue);

                if (ShowLightmap) {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(Game.Materials.ScreenSpaceBitmap, blendState: BlendState.Opaque),
                        samplerState: SamplerState.LinearClamp
                    )) {
                        bb.Add(new BitmapDrawCall(                            
                            VisualizeForeground 
                                ? ForegroundLightmap
                                : Lightmap, 
                            Vector2.Zero
                        ));
                    }
                } else {
                    using (var bb = BitmapBatch.New(
                        group, 1,
                        Game.Materials.Get(
                            ShowGBuffer
                                ? Game.Materials.ScreenSpaceBitmap
                                : Game.Materials.ScreenSpaceLightmappedBitmap,
                            blendState: 
                                ShowGBuffer
                                ? BlendState.Opaque
                                : BlendState.AlphaBlend
                        ),
                        samplerState: SamplerState.PointClamp
                    )) {
                        if (!VisualizeForeground) {
                            var dc = new BitmapDrawCall(
                                Background, Vector2.Zero, Color.White * (ShowGBuffer ? 0.7f : 1.0f)
                            );
                            dc.Textures = new TextureSet(dc.Textures.Texture1, Lightmap);
                            bb.Add(dc);
                        }

                        if (!ShowGBuffer) {
                            var dc = new BitmapDrawCall(
                                Foreground, Vector2.Zero
                            );
                            dc.Textures = new TextureSet(dc.Textures.Texture1, ForegroundLightmap);
                            dc.SortOrder = 1;
                            bb.Add(dc);
                        }
                    }

                    using (var addBatch = BitmapBatch.New(
                        group, 3,
                        Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap, blendState: RenderStates.AdditiveBlend
                        ),
                        SamplerState.LinearClamp
                    )) {
                        foreach (var proj in Projectiles) {
                            var pos = proj.Light.Position;
                            var color = proj.Light.Color;
                            var xy = new Vector2(pos.X, pos.Y - (Environment.ZToYMultiplier * (pos.Z - Environment.GroundZ)));
                            float opacity = proj.Light.Opacity * color.W * 4f;
                            color.X *= opacity;
                            color.Y *= opacity;
                            color.Z *= opacity;
                            color.W = opacity;

                            var dc = new BitmapDrawCall(Spark, xy, new Color(color));
                            dc.Origin = new Vector2(0.5f, 0.5f);
                            dc.ScaleF = 0.4f * proj.Size;
                            dc.Rotation = ((proj.Index * 4) + proj.Age) / 24f;

                            addBatch.Add(dc);
                        }
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
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            VisualizeForeground 
                                ? ForegroundRenderer.DistanceField.Texture
                                : Renderer.DistanceField.Texture, 
                            Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            new Color(255, 255, 255, 255), dfScale
                        ));
                }

                if (ShowGBuffer) {
                    using (var bb = BitmapBatch.New(
                        group, 4, Game.Materials.Get(
                            Game.Materials.ScreenSpaceBitmap,
                            blendState: BlendState.Opaque
                        ),
                        samplerState: SamplerState.PointClamp
                    ))
                        bb.Add(new BitmapDrawCall(
                            VisualizeForeground 
                                ? ForegroundRenderer.GBuffer.Texture
                                : Renderer.GBuffer.Texture, 
                            Vector2.Zero, new Bounds(Vector2.Zero, Vector2.One), 
                            Color.White, 
                            VisualizeForeground
                                ? ForegroundScaleRatio
                                : BackgroundScaleRatio
                        ));
                }

                var ir = new ImperativeRenderer(
                    frame, Game.Materials, layer++,
                    blendState: BlendState.Opaque,
                    samplerState: SamplerState.LinearClamp
                );
                ir.DrawString(
                    Game.Font, string.Format(
@"Exposure {0:00.000}
{1:0000} Projectiles",
                        exposure, Projectiles.Count
                    ), new Vector2(3, 3), scale: 0.5f
                );
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.L))
                    ShowLightmap = !ShowLightmap;

                if (KeyWasPressed(Keys.G))
                    ShowGBuffer = !ShowGBuffer;

                if (KeyWasPressed(Keys.F))
                    VisualizeForeground = !VisualizeForeground;

                if (KeyWasPressed(Keys.D))
                    ShowDistanceField = !ShowDistanceField;

                if (KeyWasPressed(Keys.R))
                    Deterministic = !Deterministic;

                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                LightZ = ((ms.ScrollWheelValue / 4096.0f) * Environment.MaximumZ) + Environment.GroundZ;

                if (LightZ < -64f)
                    LightZ = -64f;

                var mousePos = new Vector3(ms.X, ms.Y, LightZ);

                Projectile proj;
                if (ms.LeftButton == ButtonState.Pressed) {
                    var projectileTarget = new Vector3(636, 275, 80f);
                    proj = new Projectile(mousePos, projectileTarget);
                    Environment.Lights.Add(proj.Light);
                    ForegroundEnvironment.Lights.Add(proj.Light);
                    Projectiles.Add(proj);
                }

                if (Deterministic)
                    ((SphereLightSource)Environment.Lights[0]).Position = 
                        ((SphereLightSource)ForegroundEnvironment.Lights[0]).Position = 
                        new Vector3(Width / 2f, Height / 2f, 200f);
                else
                    ((SphereLightSource)Environment.Lights[0]).Position = 
                        ((SphereLightSource)ForegroundEnvironment.Lights[0]).Position = 
                        mousePos;

                using (var e = Projectiles.GetEnumerator())
                while (e.GetNext(out proj)) {
                    if (!proj.Update()) {
                        Environment.Lights.Remove(proj.Light);
                        ForegroundEnvironment.Lights.Remove(proj.Light);
                        e.RemoveCurrent();
                    }
                }
            }
        }

        public override string Status {
            get {
                var pls = (SphereLightSource)Environment.Lights[0];
                return string.Format(
                    "L@{1:0000},{2:0000},{0:000.0}", 
                    LightZ, pls.Position.X, pls.Position.Y
                );
            }
        }
    }
}
