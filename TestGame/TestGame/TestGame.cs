using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
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

namespace TestGame {
    public class TestGame : MultithreadedGame {
        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet ScreenMaterials;

        public KeyboardState PreviousKeyboardState, KeyboardState;

        public readonly Scene[] Scenes;
        public int ActiveSceneIndex = 0;

        public TestGame () {
            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1257;
            Graphics.PreferredBackBufferHeight = 1250;
            Graphics.SynchronizeWithVerticalRetrace = true;
            // Graphics.SynchronizeWithVerticalRetrace = false;
            Graphics.PreferMultiSampling = false;

            Content.RootDirectory = "Content";

            UseThreadedDraw = true;
            IsFixedTimeStep = false;

            PreviousKeyboardState = Keyboard.GetState();

            Scenes = new Scene[] {
                new Goat(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new LightingTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new ReceiverTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new ParticleLight(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new RampTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new HeightVolumeTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new SoulcasterTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight)
            };

            ActiveSceneIndex = 6;
        }

        protected override void LoadContent () {
            base.LoadContent();

            ScreenMaterials = new DefaultMaterialSet(Services) {
                ViewportScale = new Vector2(1, 1),
                ViewportPosition = new Vector2(0, 0),
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0, GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, 0,
                    0, 1
                )
            };

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
            }

            Scenes[ActiveSceneIndex].Update(gameTime);

            Window.Title = String.Format("Scene {0}: {1} {2}", ActiveSceneIndex, Scenes[ActiveSceneIndex].GetType().Name, Scenes[ActiveSceneIndex].Status);

            PreviousKeyboardState = KeyboardState;

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            Scenes[ActiveSceneIndex].Draw(frame);
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

        public abstract string Status { get; }
    }
}
