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
using Squared.Render.RasterShape;
using Squared.Util;

namespace TestGame.Scenes {
    // These aren't illuminant specific but who cares
    public class RasterShapeSpeed : Scene {
        Toggle BlendInLinearSpace, UseTexture, UseGeometry, Simple, Rectangles;

        Slider FillPower, FillOffset;

        Texture2D Texture;

        public RasterShapeSpeed (TestGame game, int width, int height)
            : base(game, width, height) {

            FillOffset.Min = -1f;
            FillOffset.Max = 1f;
            FillOffset.Speed = 0.05f;
            FillPower.Value = 1;
            FillPower.Min = 0.05f;
            FillPower.Max = 10f;
            FillPower.Speed = 0.05f;
        }

        public override void LoadContent () {
            Texture = Game.TextureLoader.Load("template");
        }

        public override void UnloadContent () {
        }

        public override void Draw (Squared.Render.Frame frame) {
            var vt = Game.Materials.ViewTransform;
            vt.Position = new Vector2(64, 64);
            vt.Scale = Vector2.One * 1.2f;

            var batch = BatchGroup.New(
                frame, 0,
                materialSet: Game.Materials
            );
            batch.SetViewTransform(in vt);

            var ir = new ImperativeRenderer(batch, Game.Materials, blendState: BlendState.AlphaBlend);
            ir.Clear(layer: 0, color: new Color(0, 32, 48));
            ir.RasterBlendInLinearSpace = BlendInLinearSpace.Value;

            int count = UseGeometry ? 32 : 64;
            const float step = 40;
            const float radiusBase = 10;

            for (int y = 0; y < count; y++) {
                for (int x = 0; x < count; x++) {
                    var center = new Vector2(x * step, y * step);
                    var radius = Vector2.One * (radiusBase + (x + y) / 2);

                    var c1 = new Color(y % 2 == 0 ? 1.0f : 0.0f, x % 2 == 0 ? 1.0f : 0.0f, 1.0f, 1.0f);
                    var c2 = Simple ? c1 : Color.Black;

                    if (UseGeometry)
                        ir.FillCircle(center, 0, radius.X, c1, c2);
                    else if (Rectangles)
                        ir.RasterizeRectangle(
                            center - radius, center + radius, 0f, 
                            c1, c2, 
                            texture: (UseTexture && !Simple) ? Texture : null,
                            fill: new RasterFillSettings {
                                Offset = FillOffset.Value,
                                GradientPower = FillPower.Value
                            }
                        );
                    else
                        ir.RasterizeEllipse(
                            center, radius, c1, c2, texture: (UseTexture && !Simple) ? Texture : null, 
                            fill: new RasterFillSettings {
                                Offset = FillOffset.Value,
                                GradientPower = FillPower.Value
                            }
                        );
                }
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
