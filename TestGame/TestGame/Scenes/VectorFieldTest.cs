using System;
using System.Collections.Generic;
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
    public class VectorFieldTest : Scene {
        VectorField Field;
        ParticleEngine Engine;
        ParticleSystem System;
        Texture2D Background, FieldTexture;

        Toggle Running;
        Slider FieldScale, FieldIntensity, Opacity;
        [Items("Bitmap")]
        [Items("Warp")]
        [Items("Particles")]
        Dropdown<string> RenderMode;

        Vector2 FieldPosition;

        public VectorFieldTest (TestGame game, int width, int height)
            : base(game, width, height) {
            Running.Value = true;

            Running.Key = Keys.Space;

            FieldScale.Min = 0.1f;
            FieldScale.Max = 4;
            FieldScale.Value = 1;
            FieldScale.Speed = 0.05f;

            FieldIntensity.Min = -256;
            FieldIntensity.Max = 256;
            FieldIntensity.Value = -40;
            FieldIntensity.Speed = 0.5f;

            Opacity.Min = 0f;
            Opacity.Max = 1f;
            Opacity.Value = 1f;
            Opacity.Speed = 0.05f;

            RenderMode.Key = Keys.M;
        }

        public override void LoadContent () {
            Engine = new ParticleEngine(
                Game.RenderCoordinator, Game.Materials, 
                new ParticleEngineConfiguration(), Game.ParticleMaterials
            );

            FieldTexture = Game.TextureLoader.Load("vector-field", new TextureLoadOptions {
                Premultiply = false,
                GenerateMips = true
            }, true);
            Field = new VectorField(Game.RenderCoordinator, FieldTexture, ownsTexture: false);
            Background = Game.TextureLoader.Load("vector-field-background");

            SetupParticleSystem();

            System.OnDeviceReset += InitializeSystem;
            Reset();
        }

        public override void UnloadContent () {
            Reset();
            System.Dispose();
            Field.Dispose();
            Engine.Dispose();
        }

        void SetupParticleSystem () {
            var sz = new Vector3(Width, Height, 0);
            var fireball = Game.TextureLoader.Load("fireball");
            var fireballRect = fireball.BoundsFromRectangle(new Rectangle(0, 0, 34, 21));
            var spark = Game.TextureLoader.Load("spark");

            const int opacityFromLife = 200;

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration() {
                    Appearance = new ParticleAppearance(texture: spark) {
                        RelativeSize = false
                    },
                    Size = Vector2.One * 2.6f,
                    RotationFromVelocity = true,
                    Color = {
                        OpacityFromLife = opacityFromLife / 60f,
                    },
                    Collision = {
                        LifePenalty = 1
                    },
                    MaximumVelocity = 2048,
                }
            ) {
                Transforms = {
                }
            };
        }

        public void Reset () {
            InitializeSystem(System);

            GC.Collect();
        }

        private void InitializeSystem (ParticleSystem system) {
            system.Configuration.Collision.DistanceFieldMaximumZ = 256;
            system.Reset();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            // FIXME
            if (Game.IlluminantMaterials.ScreenSpaceVectorWarp == null)
                return;

            if (Running)
                System.Update(frame, -2);

            Game.RenderCoordinator.BeforePrepare((frame) => {
                Game.IlluminantMaterials.ScreenSpaceVectorWarp.Parameters["FieldIntensity"].SetValue(
                    new Vector3(FieldIntensity)
                );
            });

            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Clear(layer: 0, color: Color.Black);

            ir.Draw(Background, Vector2.Zero, layer: 1);

            switch (RenderMode.Value) {
                case "Bitmap":
                case "Warp":
                    var material =
                        (RenderMode.Value == "Bitmap")
                            ? Game.Materials.GetBitmapMaterial(false, blendState: BlendState.NonPremultiplied)
                            : Game.IlluminantMaterials.ScreenSpaceVectorWarp;
                    var dc = new BitmapDrawCall(
                        FieldTexture, FieldPosition, FieldScale
                    ) {
                        Origin = Vector2.One * 0.5f,
                        MultiplyColor = Color.White * Opacity
                    };
                    dc.Textures = new TextureSet(Background, FieldTexture);
                    using (var bb = BitmapBatch.New(frame, 2, material, SamplerState.LinearClamp, SamplerState.LinearClamp))
                        bb.Add(dc);
                    break;
                case "Particles":
                    break;
            }

            // if (Running)
                System.Render(
                    frame, 3, 
                    blendState: RenderStates.AdditiveBlend
                );
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                const float step = 0.1f;

                if (KeyWasPressed(Keys.R))
                    Reset();

                for (var i = 0; i < 9; i++) {
                    if (i >= System.Transforms.Count)
                        break;
                    var k = Keys.D1 + i;
                    if (KeyWasPressed(k))
                        System.Transforms[i].IsActive = !System.Transforms[i].IsActive;
                }

                var time = (float)Time.Seconds;

                var sz = new Vector3(Width, Height, 0);

                var ms = Game.MouseState;
                Game.IsMouseVisible = true;

                if (!Game.IsMouseOverUI)
                    FieldPosition = new Vector2(ms.X, ms.Y);
            }
        }
    }
}
