﻿using System;
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
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.DistanceField;
using Squared.Util;

namespace TestGame.Scenes {
    public class JumpFlooding : Scene {
        Texture2D Sprite, CPUDistanceField;
        RenderTarget2D GPUDistanceField;
        bool NeedGenerateGPUField = true;

        [Items("Image")]
        [Items("Distance Field")]
        [Items("Outline")]
        Dropdown<string> Mode;

        Slider OutlineThickness, OutlineSoftness, OutlinePower, OutlineOffset, Scale, SmoothingLevel;
        Toggle UseGPUField;
        JumpFlood.GPUScratchSurfaces JumpScratchSurfaces;

        public JumpFlooding (TestGame game, int width, int height)
            : base(game, width, height) {

            Mode.Key = Keys.K;
            Mode.Value = "Outline";
            UseGPUField.Key = Keys.G;
            UseGPUField.Value = true;
            OutlineThickness.Min = -32f;
            OutlineThickness.Max = 256f;
            OutlineThickness.Value = 8f;
            OutlineThickness.Speed = 0.5f;
            OutlineSoftness.Min = 0.1f;
            OutlineSoftness.Max = 128f;
            OutlineSoftness.Value = 4f;
            OutlineSoftness.Speed = 0.05f;
            OutlinePower.Min = 0.05f;
            OutlinePower.Max = 4f;
            OutlinePower.Value = 1f;
            OutlinePower.Speed = 0.1f;
            OutlineOffset.Min = -32f;
            OutlineOffset.Max = 32f;
            OutlineOffset.Value = 0f;
            OutlineOffset.Speed = 0.5f;
            SmoothingLevel.Min = 0f;
            SmoothingLevel.Max = 1f;
            SmoothingLevel.Value = 1.0f;
            SmoothingLevel.Speed = 0.05f;
            SmoothingLevel.Changed += (s, e) => NeedGenerateGPUField = true;
            Scale.Min = 0.1f;
            Scale.Max = 2f;
            Scale.Value = 1.0f;
            Scale.Speed = 0.05f;
        }

        public override void LoadContent () {
            Sprite = Game.TextureLoader.LoadSync("test-sprite", new TextureLoadOptions {
                GenerateDistanceField = true,
                Premultiply = true
            }, true, false);
            CPUDistanceField = Game.TextureLoader.GetDistanceField(Sprite);
            NeedGenerateGPUField = true;
            GPUDistanceField = new RenderTarget2D(
                Game.GraphicsDevice, Sprite.Width, Sprite.Height, true, SurfaceFormat.HalfSingle, DepthFormat.None,
                0, RenderTargetUsage.DiscardContents
            );
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var ir = new ImperativeRenderer(frame, Game.Materials);

            if (NeedGenerateGPUField) {
                JumpFlood.GenerateDistanceField(
                    ref ir, Sprite, GPUDistanceField, ref JumpScratchSurfaces, 
                    layer: -1, smoothingLevel: SmoothingLevel
                );
                NeedGenerateGPUField = false;
            }

            ir.Clear(layer: 0, color: Color.DeepSkyBlue);

            var material = (Mode.Value == "Outline")
                ? Game.Materials.DistanceFieldOutlinedBitmap
                : Game.Materials.ScreenSpaceBitmap;
            var distanceField = UseGPUField ? GPUDistanceField : CPUDistanceField;
            var textures = new TextureSet(
                Mode.Value == "Distance Field" ? distanceField : Sprite,
                distanceField
            );
            var dc = new BitmapDrawCall(textures, Vector2.Zero) {
                UserData = new Vector4(1, 0, 0, 1),
                TextureRegion = Bounds.Unit.Expand(0.1f, 0.1f),
                TextureRegion2 = Bounds.Unit.Expand(0.1f, 0.1f),
                ScaleF = Scale.Value
            };
            ir.Parameters.Add("ShadowOffset", new Vector2(OutlineOffset.Value, OutlineOffset.Value));
            ir.Parameters.Add("OutlineRadiusSoftnessAndPower", new Vector3(OutlineThickness.Value, OutlineSoftness.Value, OutlinePower.Value));
            ir.Draw(dc, layer: 1, material: material, blendState: BlendState.Opaque, samplerState2: SamplerState.LinearClamp);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
