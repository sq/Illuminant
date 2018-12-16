﻿using System;
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
using Nuke = NuklearDotNet.Nuklear;

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
                Game.Content, Game.RenderCoordinator, Game.Materials, 
                new ParticleEngineConfiguration(), Game.ParticleMaterials
            );

            FieldTexture = Game.Content.Load<Texture2D>("vector-field");
            Field = new VectorField(Game.RenderCoordinator, FieldTexture);
            Background = Game.Content.Load<Texture2D>("vector-field-background");

            SetupParticleSystem();

            System.OnDeviceReset += InitializeSystem;
            Reset();
        }

        void SetupParticleSystem () {
            var sz = new Vector3(Width, Height, 0);
            var fireball = Game.Content.Load<Texture2D>("fireball");
            var fireballRect = fireball.BoundsFromRectangle(new Rectangle(0, 0, 34, 21));
            var spark = Game.Content.Load<Texture2D>("spark");

            const int opacityFromLife = 200;

            System = new ParticleSystem(
                Engine,
                new ParticleSystemConfiguration(
                    attributeCount: 1
                ) {
                    Texture = spark,
                    Size = Vector2.One * 2.6f,
                    RotationFromVelocity = true,
                    OpacityFromLife = opacityFromLife / 60f,
                    MaximumVelocity = 2048,
                    CollisionLifePenalty = 1,
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
            system.Configuration.DistanceFieldMaximumZ = 256;
            system.Clear();
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            if (Running)
                System.Update(frame, -2);

            Game.RenderCoordinator.BeforePrepare(() => {
                Game.IlluminantMaterials.ScreenSpaceVectorWarp.Effect.Parameters["FieldIntensity"].SetValue(
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
                    dc.Textures = new TextureSet(FieldTexture, Background);
                    using (var bb = BitmapBatch.New(frame, 2, material, SamplerState.LinearClamp, SamplerState.LinearClamp))
                        bb.Add(dc);
                    break;
                case "Particles":
                    break;
            }

            // if (Running)
                System.Render(
                    frame, 3, 
                    material: Engine.ParticleMaterials.AttributeColor,
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