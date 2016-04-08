
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
    public class DistanceFieldEditor : Scene {
        LightingEnvironment Environment;
        LightingRenderer Renderer;

        bool ShowOutlines = false;
        bool ShowSurfaces = true;

        public DistanceFieldEditor (TestGame game, int width, int height)
            : base(game, 256, 256) {
        }

        public override void LoadContent () {
            Environment = new LightingEnvironment();

            Renderer = new LightingRenderer(
                Game.Content, Game.RenderCoordinator, Game.Materials, Environment, 
                new RendererConfiguration(
                    Width, Height, true,
                    Width, Height, 256, true
                ) {
                    RenderScale = 1.0f,
                    DistanceFieldResolution = 0.5f,
                    DistanceFieldUpdateRate = 8
                }
            );

            Environment.GroundZ = 0;
            Environment.MaximumZ = 256;
            Environment.ZToYMultiplier = 2.5f;

            var offset = new Vector3(32, 32, 32);
            var size = new Vector3(24, 24, 24);

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Box,
                offset, size
            ));

            offset.X += (size.X * 2) + 8;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid,
                offset, size                
            ));

            offset.X += (size.X * 2) + 8;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Cylinder,
                offset, size                
            ));
        }

        private void Visualize (Frame frame, ref int layer, float x, float y, float size, Vector3 gaze) {
            var rect = Bounds.FromPositionAndSize(new Vector2(x, y), new Vector2(size, size));
            Renderer.VisualizeDistanceField(
                rect, gaze, frame, layer++
            );
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            Renderer.UpdateFields(frame, -1);

            var visSize = Math.Min(
                Game.Graphics.PreferredBackBufferWidth / 3.1f,
                Game.Graphics.PreferredBackBufferHeight / 2.1f
            );

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, Color.Blue);

            if (ShowSurfaces) {
                const float pad = 8;
                int layer = 1;

                Visualize(frame, ref layer, 0, 0, visSize, -Vector3.UnitX);
                Visualize(frame, ref layer, 0, visSize + pad, visSize, Vector3.UnitX);

                Visualize(frame, ref layer, visSize + pad, 0, visSize, -Vector3.UnitY);
                Visualize(frame, ref layer, visSize + pad, visSize + pad, visSize, Vector3.UnitY);

                Visualize(frame, ref layer, (visSize + pad) * 2, 0, visSize, -Vector3.UnitZ);
                Visualize(frame, ref layer, (visSize + pad) * 2, visSize + pad, visSize, Vector3.UnitZ);
            }
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                var ms = Mouse.GetState();
                Game.IsMouseVisible = true;

                Renderer.InvalidateFields();
            }
        }

        public override string Status {
            get {
                return string.Format(
                    ""
                );
            }
        }
    }
}
