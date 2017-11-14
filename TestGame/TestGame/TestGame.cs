using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Game;
using Squared.Illuminant;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;
using TestGame.Scenes;
using ThreefoldTrials.Framework;

namespace TestGame {
    public class TestGame : MultithreadedGame {
        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials;

        public KeyboardState PreviousKeyboardState, KeyboardState;

        public SpriteFont Font;
        public Texture2D RampTexture;

        public readonly Scene[] Scenes;
        public int ActiveSceneIndex = 1;

        private int LastPerformanceStatPrimCount = 0;

        public TestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
            Graphics.SynchronizeWithVerticalRetrace = false;
            // Graphics.IsFullScreen = true;
            // Graphics.SynchronizeWithVerticalRetrace = false;
            Graphics.PreferMultiSampling = false;

            Content.RootDirectory = "Content";

            UseThreadedDraw = true;
            IsFixedTimeStep = false;

            if (IsFixedTimeStep) {
                TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 30);
            }

            PreviousKeyboardState = Keyboard.GetState();

            Scenes = new Scene[] {
                new HeightVolumeTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new TwoPointFiveDTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new SC3(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new DistanceFieldEditor(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new ScrollingGeo(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight)
            };
        }

        protected override void LoadContent () {
            base.LoadContent();

            Font = Content.Load<SpriteFont>("Font");
            Materials = new DefaultMaterialSet(Services);
            RampTexture = Content.Load<Texture2D>("light_ramp");

            foreach (var scene in Scenes)
                scene.LoadContent();
        }

        protected override void Update (GameTime gameTime) {
            KeyboardState = Keyboard.GetState();

            if (IsActive) {
                if (KeyboardState.IsKeyDown(Keys.OemOpenBrackets) && !PreviousKeyboardState.IsKeyDown(Keys.OemOpenBrackets))
                    ActiveSceneIndex = Arithmetic.Wrap(ActiveSceneIndex - 1, 0, Scenes.Length - 1);
                else if (KeyboardState.IsKeyDown(Keys.OemCloseBrackets) && !PreviousKeyboardState.IsKeyDown(Keys.OemCloseBrackets))
                    ActiveSceneIndex = Arithmetic.Wrap(ActiveSceneIndex + 1, 0, Scenes.Length - 1);
                else if (KeyboardState.IsKeyDown(Keys.OemTilde) && !PreviousKeyboardState.IsKeyDown(Keys.OemTilde)) {
                    Graphics.SynchronizeWithVerticalRetrace = !Graphics.SynchronizeWithVerticalRetrace;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                }
                else if (KeyboardState.IsKeyDown(Keys.OemPipe) && !PreviousKeyboardState.IsKeyDown(Keys.OemPipe)) {
                    UniformBinding.ForceCompatibilityMode = !UniformBinding.ForceCompatibilityMode;
                }
            }

            Scenes[ActiveSceneIndex].Update(gameTime);

            PerformanceStats.Record(this);

            Window.Title = String.Format("Scene {0}: {1}", ActiveSceneIndex, Scenes[ActiveSceneIndex].GetType().Name);

            PreviousKeyboardState = KeyboardState;

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, -9999, Materials.Clear, Color.Black);

            Scenes[ActiveSceneIndex].Draw(frame);

            var ir = new ImperativeRenderer(
                frame, Materials, 
                blendState: BlendState.Opaque, 
                depthStencilState: DepthStencilState.None, 
                rasterizerState: RasterizerState.CullNone,
                worldSpace: false,
                layer: 9999
            );

            DrawPerformanceStats(ref ir);
        }

        private void DrawPerformanceStats (ref ImperativeRenderer ir) {
            const float scale = 0.75f;
            var text = PerformanceStats.GetText(this, -LastPerformanceStatPrimCount);

            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = Font.LayoutString(text, buffer, scale: scale);
                var layoutSize = layout.Size;
                var position = new Vector2(Graphics.PreferredBackBufferWidth - (232 * scale), 30f).Floor();
                var dc = layout.DrawCalls;

                // fill quad + text quads
                LastPerformanceStatPrimCount = (layout.Count * 2) + 2;

                ir.FillRectangle(
                    Bounds.FromPositionAndSize(position, layoutSize),
                    Color.Black
                );
                ir.Layer += 1;
                ir.DrawMultiple(dc, position);
            }
        }
    }

    public abstract class Scene {
        public readonly TestGame Game;
        public readonly int Width, Height;

        public Scene (TestGame game, int width, int height) {
            Game = game;
            Width = width;
            Height = height;
        }

        public abstract void LoadContent ();
        public abstract void Draw (Frame frame);
        public abstract void Update (GameTime gameTime);

        protected bool KeyWasPressed (Keys key) {
            return Game.KeyboardState.IsKeyDown(key) && Game.PreviousKeyboardState.IsKeyUp(key);
        }
    }
}
