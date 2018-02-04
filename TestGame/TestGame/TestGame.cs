using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
using Squared.Render.Text;
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
        public int ActiveSceneIndex = 7;

        private int LastPerformanceStatPrimCount = 0;

        public TestGame () {
            // UniformBinding.ForceCompatibilityMode = true;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = false;
            Graphics.IsFullScreen = false;

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
                new ScrollingGeo(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new SimpleParticles(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new LightProbeTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new GlobalIlluminationTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight)
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
                var alt = KeyboardState.IsKeyDown(Keys.LeftAlt) || KeyboardState.IsKeyDown(Keys.RightAlt);
                var wasAlt = PreviousKeyboardState.IsKeyDown(Keys.LeftAlt) || PreviousKeyboardState.IsKeyDown(Keys.RightAlt);

                if (KeyboardState.IsKeyDown(Keys.OemOpenBrackets) && !PreviousKeyboardState.IsKeyDown(Keys.OemOpenBrackets))
                    ActiveSceneIndex = Arithmetic.Wrap(ActiveSceneIndex - 1, 0, Scenes.Length - 1);
                else if (KeyboardState.IsKeyDown(Keys.OemCloseBrackets) && !PreviousKeyboardState.IsKeyDown(Keys.OemCloseBrackets))
                    ActiveSceneIndex = Arithmetic.Wrap(ActiveSceneIndex + 1, 0, Scenes.Length - 1);
                else if (KeyboardState.IsKeyDown(Keys.OemTilde) && !PreviousKeyboardState.IsKeyDown(Keys.OemTilde)) {
                    Graphics.SynchronizeWithVerticalRetrace = !Graphics.SynchronizeWithVerticalRetrace;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                }
                else if (
                    (KeyboardState.IsKeyDown(Keys.Enter) && alt) &&
                    (!PreviousKeyboardState.IsKeyDown(Keys.Enter) || !wasAlt)
                ) {
                    Graphics.IsFullScreen = !Graphics.IsFullScreen;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                }
                else if (KeyboardState.IsKeyDown(Keys.OemPipe) && !PreviousKeyboardState.IsKeyDown(Keys.OemPipe)) {
                    UniformBinding.ForceCompatibilityMode = !UniformBinding.ForceCompatibilityMode;
                }
            }

            Scenes[ActiveSceneIndex].UpdateSettings();
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

            Scenes[ActiveSceneIndex].DrawSettings(frame, 9998);

            DrawPerformanceStats(ref ir);
        }

        private void DrawPerformanceStats (ref ImperativeRenderer ir) {
            const float scale = 0.75f;
            var text = PerformanceStats.GetText(this, -LastPerformanceStatPrimCount);
            text += string.Format("{0}VSync {1}", Environment.NewLine, Graphics.SynchronizeWithVerticalRetrace ? "On" : "Off");

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
        internal List<ISetting> Settings = new List<ISetting>();

        public readonly TestGame Game;
        public readonly int Width, Height;

        public Scene (TestGame game, int width, int height) {
            Game = game;
            Width = width;
            Height = height;

            var tSetting = typeof(ISetting);
            foreach (var f in GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
                if (!tSetting.IsAssignableFrom(f.FieldType))
                    continue;

                var setting = (ISetting)Activator.CreateInstance(f.FieldType);
                setting.Name = f.Name;
                Settings.Add(setting);
                f.SetValue(this, setting);
            }
        }

        public abstract void LoadContent ();
        public abstract void Draw (Frame frame);
        public abstract void Update (GameTime gameTime);

        public void UpdateSettings () {
            foreach (var s in Settings)
                s.Update(this);
        }

        public void DrawSettings (IBatchContainer container, int layer) {
            float scale = 0.75f;

            var count = Settings.Count;
            var lineHeight = Game.Font.LineSpacing * scale;

            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(4096)) {
                var ir = new ImperativeRenderer(
                    container, Game.Materials,
                    blendState: BlendState.AlphaBlend,
                    depthStencilState: DepthStencilState.None,
                    rasterizerState: RasterizerState.CullNone,
                    worldSpace: false,
                    layer: layer
                );

                float y = Game.Graphics.PreferredBackBufferHeight - (count * lineHeight) - 10;
                var sle = new StringLayoutEngine {
                    position = new Vector2(10, y),
                    scale = scale,
                    color = Color.White,
                    buffer = new ArraySegment<BitmapDrawCall>(buffer.Data)
                };

                sle.Initialize();

                var gs = new SpriteFontGlyphSource(Game.Font);
                foreach (var s in Settings) {
                    sle.AppendText(gs, s.ToString());
                    sle.AppendText(gs, Environment.NewLine);
                }

                var sl = sle.Finish();
                ir.DrawMultiple(sl.DrawCalls);
            }
        }

        internal bool KeyWasPressed (Keys key) {
            return Game.KeyboardState.IsKeyDown(key) && Game.PreviousKeyboardState.IsKeyUp(key);
        }
    }

    public interface ISetting {
        void Update (Scene s);
        string Name { get; set; }
    }

    public abstract class Setting<T> : ISetting
        where T : IEquatable<T>
    {
        public event EventHandler<T> Changed;
        public string Name { get; set; }
        protected T _Value;

        public virtual T Value { 
            get { return _Value; }
            set {
                if (!_Value.Equals(value)) {
                    _Value = value;
                    if (Changed != null)
                        Changed(this, value);
                }
            }
        }

        public abstract void Update (Scene s);

        public static implicit operator T (Setting<T> setting) {
            return setting.Value;
        }
    }

    public class Toggle : Setting<bool> {
        public Keys Key;

        public override void Update (Scene s) {
            if (s.KeyWasPressed(Key))
                Value = !Value;
        }

        public override string ToString () {
            return string.Format("{0,-2} {1} {2}", Key, Value ? "+" : "-", Name);
        }
    }

    public class Slider : Setting<float> {
        public Keys MinusKey, PlusKey;
        public float? Min, Max;
        public float Speed = 1;        

        public override void Update (Scene s) {
            float delta = 0;

            if (s.KeyWasPressed(MinusKey))
                delta = -Speed;
            else if (s.KeyWasPressed(PlusKey))
                delta = Speed;
            else
                return;

            var newValue = Value + delta;
            if (Min.HasValue)
                newValue = Math.Max(newValue, Min.Value);
            if (Max.HasValue)
                newValue = Math.Min(newValue, Max.Value);

            if (Value == newValue)
                return;

            Value = newValue;
        }

        public override string ToString () {
            string formattedValue;
            if (Speed < 1) {
                formattedValue = string.Format("{0:00.000}", Value);
            } else {
                formattedValue = string.Format("{0:00000}", Value);
            }
            return string.Format("{0,-2} {1:0} {2} {3,2}", MinusKey, formattedValue, Name, PlusKey);
        }
    }
}
