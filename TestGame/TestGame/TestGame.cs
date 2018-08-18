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
using Nuke = NuklearDotNet.Nuklear;

namespace TestGame {
    public class TestGame : MultithreadedGame {
        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials;
        public NuklearService Nuklear;

        public KeyboardState PreviousKeyboardState, KeyboardState;
        public MouseState PreviousMouseState, MouseState;

        public SpriteFont Font;
        public Texture2D RampTexture;

        public readonly Scene[] Scenes;
        public int ActiveSceneIndex;

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
                new GlobalIlluminationTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new DungeonGI(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new ParticleLights(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new DynamicObstructions(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
            };

            ActiveSceneIndex = Scenes.Length - 1;
        }

        protected unsafe void UIScene () {
            var ctx = Nuklear.Context;

            var scene = Scenes[ActiveSceneIndex];
            var settings = scene.Settings;
            if (Nuke.nk_begin(
                ctx, "Settings", new NuklearDotNet.NkRect(Graphics.PreferredBackBufferWidth - 504, Graphics.PreferredBackBufferHeight - 404, 500, 400), 
                (uint)(NuklearDotNet.NkPanelFlags.Title | NuklearDotNet.NkPanelFlags.Border | NuklearDotNet.NkPanelFlags.Movable | NuklearDotNet.NkPanelFlags.Minimizable)
            ) != 0) {
                foreach (var s in settings) {
                    Nuke.nk_layout_row_dynamic(ctx, 0, 1);
                    var name = s.GetLabelUTF8();
                    var toggle = s as Toggle;
                    var slider = s as Slider;
                    if (toggle != null) {
                        // FIXME: Why is this backwards?
                        int result = Nuke.nk_check_text(ctx, name.pText, name.Length, toggle.Value ? 0 : 1);
                        toggle.Value = result == 0;
                    } else if (slider != null) {
                        Nuke.nk_label(ctx, name.pText, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                        var bounds = Nuke.nk_widget_bounds(ctx);
                        slider.Value = Nuke.nk_slide_float(ctx, slider.Min.GetValueOrDefault(0), slider.Value, slider.Max.GetValueOrDefault(1), slider.Speed);
                        if (Nuke.nk_input_is_mouse_hovering_rect(&ctx->input, bounds) != 0) {
                            using (var utf8 = new UTF8String(string.Format("   {0:####0.00}", slider.Value)))
                                Nuke.nk_tooltip(ctx, utf8.pText);
                        }
                    }
                }
            }

            Nuke.nk_end(ctx);
        }

        protected unsafe void UpdateNuklearInput () {
            var ctx = Nuklear.Context;
            Nuke.nk_input_begin(ctx);
            if ((MouseState.X != PreviousMouseState.X) || (MouseState.Y != PreviousMouseState.Y))
                Nuke.nk_input_motion(ctx, MouseState.X, MouseState.Y);
            if (MouseState.LeftButton != PreviousMouseState.LeftButton)
                Nuke.nk_input_button(ctx, NuklearDotNet.nk_buttons.NK_BUTTON_LEFT, MouseState.X, MouseState.Y, MouseState.LeftButton == ButtonState.Pressed ? 1 : 0);
            Nuke.nk_input_end(ctx);
        }

        protected override void LoadContent () {
            base.LoadContent();

            Font = Content.Load<SpriteFont>("Font");
            Materials = new DefaultMaterialSet(Services);
            RampTexture = Content.Load<Texture2D>("light_ramp");

            Nuklear = new NuklearService(this) {
                Font = new SpriteFontGlyphSource(Font),
                FontScale = 0.75f,
                Scene = UIScene
            };

            foreach (var scene in Scenes)
                scene.LoadContent();
        }

        protected override void Update (GameTime gameTime) {
            PreviousKeyboardState = KeyboardState;
            PreviousMouseState = MouseState;
            KeyboardState = Keyboard.GetState();
            MouseState = Mouse.GetState();

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

            UpdateNuklearInput();

            PerformanceStats.Record(this);

            Window.Title = String.Format("Scene {0}: {1}", ActiveSceneIndex, Scenes[ActiveSceneIndex].GetType().Name);

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
                samplerState: SamplerState.LinearClamp,
                worldSpace: false,
                layer: 9999
            );

            Nuklear.Render(gameTime.ElapsedGameTime.Seconds, frame, 9997);

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

        internal bool KeyWasPressed (Keys key) {
            return Game.KeyboardState.IsKeyDown(key) && Game.PreviousKeyboardState.IsKeyUp(key);
        }
    }
}
