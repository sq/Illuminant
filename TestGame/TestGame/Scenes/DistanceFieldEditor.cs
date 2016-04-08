
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
            : base(game, 1024, 1024) {
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
                    DistanceFieldResolution = 0.25f,
                    DistanceFieldUpdateRate = 1
                }
            );

            Environment.GroundZ = 0;
            Environment.MaximumZ = 1024;
            Environment.ZToYMultiplier = 2.5f;

            var offset = new Vector3(64, 64, 64);
            var size = new Vector3(48, 48, 48);

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Box,
                offset, size
            ));

            offset.X += (size.X * 2) + 16;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Ellipsoid,
                offset, size                
            ));

            offset.X += (size.X * 2) + 16;

            Environment.Obstructions.Add(new LightObstruction(
                LightObstructionType.Cylinder,
                offset, size                
            ));
        }
        
        public override void Draw (Squared.Render.Frame frame) {
            Renderer.UpdateFields(frame, -1);

            var minLength = Math.Min(
                Game.Graphics.PreferredBackBufferWidth / 2.1f,
                Game.Graphics.PreferredBackBufferHeight / 2.1f
            );

            var visSize = new Vector2(minLength, minLength);

            ClearBatch.AddNew(frame, 0, Game.Materials.Clear, Color.Blue);

            if (ShowSurfaces) {
                Renderer.VisualizeDistanceField(
                    Bounds.FromPositionAndSize(Vector2.Zero, visSize),
                    -Vector3.UnitZ,
                    frame, 1
                );

                Renderer.VisualizeDistanceField(
                    Bounds.FromPositionAndSize(new Vector2(visSize.X + 8, 0), visSize),
                    -Vector3.UnitY,
                    frame, 2
                );

                Renderer.VisualizeDistanceField(
                    Bounds.FromPositionAndSize(new Vector2(0, visSize.Y + 8), visSize),
                    -Vector3.UnitX,
                    frame, 3
                );
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
