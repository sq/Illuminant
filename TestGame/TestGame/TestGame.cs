using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        public IlluminantMaterials IlluminantMaterials;
        public ParticleMaterials ParticleMaterials;

        public KeyboardState PreviousKeyboardState, KeyboardState;
        public MouseState PreviousMouseState, MouseState;

        public Material TextMaterial;

        public FreeTypeFont Font;
        public Texture2D RampTexture;
        public RenderTarget2D UIRenderTarget;

        public readonly Scene[] Scenes;
        public int ActiveSceneIndex;

        private int LastPerformanceStatPrimCount = 0;

        public bool IsMouseOverUI = false;
        public long LastTimeOverUI;

        public readonly Dictionary<string, ColorLUT> LUTs = 
            new Dictionary<string, ColorLUT>(StringComparer.OrdinalIgnoreCase);

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
                new ParticleLights(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new DynamicObstructions(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new DitheringTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new LineLight(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new VectorFieldTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
                new LUTTest(this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight),
            };

            ActiveSceneIndex = Scenes.Length - 1;
            // ActiveSceneIndex = 1;
        }

        const float settingRowHeight = 26;

        protected unsafe void RenderSetting (ISetting s) {
            var ctx = Nuklear.Context;

            Nuke.nk_layout_row_dynamic(ctx, settingRowHeight, 1);
            var name = s.GetLabelUTF8();
            var dropdown = s as IDropdown;
            var toggle = s as Toggle;
            var slider = s as Slider;
            if (dropdown != null) {
                Nuke.nk_label(ctx, name.pText, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                int selected = dropdown.SelectedIndex;
                var rect = Nuke.nk_layout_space_bounds(ctx);
                Nuke.nk_combobox_callback(ctx, dropdown.Getter, IntPtr.Zero, &selected, dropdown.Count, 32, new NuklearDotNet.nk_vec2(rect.W, 8192));
                dropdown.SelectedIndex = selected;
            } else if (toggle != null) {
                // FIXME: Why is this backwards?
                int result = Nuke.nk_check_text(ctx, name.pText, name.Length, toggle.Value ? 0 : 1);
                toggle.Value = result == 0;
            } else if (slider != null) {
                Nuke.nk_label(ctx, name.pText, (uint)NuklearDotNet.NkTextAlignment.NK_TEXT_LEFT);
                var bounds = Nuke.nk_widget_bounds(ctx);
                slider.Value = Nuke.nk_slide_float(ctx, slider.Min.GetValueOrDefault(0), slider.Value, slider.Max.GetValueOrDefault(1), slider.Speed);
                if (Nuke.nk_input_is_mouse_hovering_rect(&ctx->input, bounds) != 0) {
                    using (var utf8 = new UTF8String("    " + slider.GetFormattedValue()))
                        Nuke.nk_tooltip(ctx, utf8.pText);
                }
            }
        }

        private UTF8String Other;

        protected unsafe void UIScene () {
            var ctx = Nuklear.Context;

            var scene = Scenes[ActiveSceneIndex];
            var settings = scene.Settings;

            var isWindowOpen = Nuke.nk_begin(
                ctx, "Settings", new NuklearDotNet.NkRect(Graphics.PreferredBackBufferWidth - 504, Graphics.PreferredBackBufferHeight - 454, 500, 450),
                (uint)(NuklearDotNet.NkPanelFlags.Title | NuklearDotNet.NkPanelFlags.Border |
                NuklearDotNet.NkPanelFlags.Movable | NuklearDotNet.NkPanelFlags.Minimizable |
                NuklearDotNet.NkPanelFlags.Scalable)
            ) != 0;

            if (isWindowOpen) {
                int i = 0;

                foreach (var s in settings)
                    RenderSetting(s);

                foreach (var kvp in settings.Groups.OrderBy(kvp => kvp.Key)) {
                    var g = kvp.Value;
                    var state = g.Visible
                        ? NuklearDotNet.nk_collapse_states.NK_MAXIMIZED
                        : NuklearDotNet.nk_collapse_states.NK_MINIMIZED;
                    var nameUtf = g.GetNameUTF8();
                    if (Nuke.nk_tree_push_hashed(
                        ctx, NuklearDotNet.nk_tree_type.NK_TREE_TAB,
                        nameUtf.pText, state, nameUtf.pText, nameUtf.Length, i
                    ) != 0) {
                        foreach (var s in g)
                            RenderSetting(s);
                        Nuke.nk_tree_state_pop(ctx);
                        // Padding
                        Nuke.nk_layout_row_dynamic(ctx, 3, 1);
                        g.Visible = (state == NuklearDotNet.nk_collapse_states.NK_MAXIMIZED);
                    }
                    i++;
                }

                i++;

                scene.UIScene();

                RenderGlobalSettings();
            }

            IsMouseOverUI = Nuke.nk_item_is_any_active(ctx) != 0;
            if (IsMouseOverUI)
                LastTimeOverUI = Time.Ticks;

            Nuke.nk_end(ctx);
        }

        UTF8String sSystem = new UTF8String("System");

        private unsafe void RenderGlobalSettings () {
            var ctx = Nuklear.Context;

            if (Nuke.nk_tree_push_hashed(ctx, NuklearDotNet.nk_tree_type.NK_TREE_TAB, sSystem.pText, NuklearDotNet.nk_collapse_states.NK_MAXIMIZED, sSystem.pText, sSystem.Length, 256) != 0) {
                using (var temp = new UTF8String("VSync")) {
                    var newVsync = Nuke.nk_check_text(ctx, temp.pText, temp.Length, Graphics.SynchronizeWithVerticalRetrace ? 0 : 1) == 0;
                    if (newVsync != Graphics.SynchronizeWithVerticalRetrace) {
                        Graphics.SynchronizeWithVerticalRetrace = newVsync;
                        Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                    }
                }

                using (var temp = new UTF8String("Fullscreen")) {
                    var newFS = Nuke.nk_check_text(ctx, temp.pText, temp.Length, Graphics.IsFullScreen ? 0 : 1) == 0;
                    if (newFS != Graphics.IsFullScreen) {
                        Graphics.IsFullScreen = newFS;
                        Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                    }
                }

                using (var temp = new UTF8String("Lazy Transform Changes")) {
                    // Materials.LazyViewTransformChanges = Nuke.nk_check_text(ctx, temp.pText, temp.Length, Materials.LazyViewTransformChanges ? 0 : 1) == 0;
                }

                Nuke.nk_tree_pop(ctx);
            }
        }

        public bool LeftMouse {
            get {
                return (MouseState.LeftButton == ButtonState.Pressed) && !IsMouseOverUI;
            }
        }

        public bool RightMouse {
            get {
                return (MouseState.RightButton == ButtonState.Pressed) && !IsMouseOverUI;
            }
        }

        protected override void LoadContent () {
            base.LoadContent();

            Font = new FreeTypeFont(RenderCoordinator, "FiraSans-Medium.otf") {
                SizePoints = 17f,
                GlyphMargin = 2
            };
            Materials = new DefaultMaterialSet(Services);
            IlluminantMaterials = new IlluminantMaterials(Materials);
            ParticleMaterials = new ParticleMaterials(Materials);
            RampTexture = Content.Load<Texture2D>("light_ramp");

            TextMaterial = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.5f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One);

            UIRenderTarget = new RenderTarget2D(
                GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                false, SurfaceFormat.Color, DepthFormat.None, 1, RenderTargetUsage.PlatformContents
            );

            Nuklear = new NuklearService(this) {
                Font = Font,
                Scene = UIScene
            };

            LoadLUTs();

            foreach (var scene in Scenes)
                scene.LoadContent();

            LastTimeOverUI = Time.Ticks;
        }

        private void LoadLUTs () {
            var identity = ColorLUT.CreateIdentity(RenderCoordinator, LUTPrecision.UInt8, LUTResolution.High);
            LUTs.Add("Identity", identity);

            var names = Directory.GetFiles(Content.RootDirectory + "\\LUTs", "*.xnb");
            foreach (var name in names) {
                var shortName = Path.GetFileNameWithoutExtension(name);
                if (LUTs.ContainsKey(shortName))
                    continue;

                var texture = Content.Load<Texture2D>("LUTs\\" + shortName);
                var lut = new ColorLUT(texture, true);
                LUTs.Add(shortName, lut);
            }
        }

        private void LoadLUT (string name) {
            var texture = Content.Load<Texture2D>("lut-" + name);
            var lut = new ColorLUT(texture, false);
            LUTs.Add(name, lut);
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

            var scene = Scenes[ActiveSceneIndex];
            scene.Settings.Update(scene);
            scene.Update(gameTime);

            PerformanceStats.Record(this);

            Window.Title = String.Format("Scene {0}: {1}", ActiveSceneIndex, scene.GetType().Name);

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            Nuklear.UpdateInput(PreviousMouseState, MouseState, PreviousKeyboardState, KeyboardState, IsMouseOverUI);

            using (var group = BatchGroup.ForRenderTarget(frame, -9990, UIRenderTarget)) {
                ClearBatch.AddNew(group, -1, Materials.Clear, clearColor: Color.Transparent);
                Nuklear.Render(gameTime.ElapsedGameTime.Seconds, group, 1);
            }

            ClearBatch.AddNew(frame, -1, Materials.Clear, Color.Black);
            Scenes[ActiveSceneIndex].Draw(frame);

            var ir = new ImperativeRenderer(
                frame, Materials, 
                blendState: BlendState.AlphaBlend,
                samplerState: SamplerState.LinearClamp,
                worldSpace: false,
                layer: 9999
            );

            var elapsedSeconds = TimeSpan.FromTicks(Time.Ticks - LastTimeOverUI).TotalSeconds;
            float uiOpacity = Arithmetic.Lerp(1.0f, 0.4f, (float)((elapsedSeconds - 0.66) * 2.25f));

            ir.Draw(UIRenderTarget, Vector2.Zero, multiplyColor: Color.White * uiOpacity);

            DrawPerformanceStats(ref ir);
        }

        private void DrawPerformanceStats (ref ImperativeRenderer ir) {
            const float scale = 0.75f;
            var text = PerformanceStats.GetText(this, -LastPerformanceStatPrimCount);
            text += string.Format("{0}VSync {1}", Environment.NewLine, Graphics.SynchronizeWithVerticalRetrace ? "On" : "Off");

            using (var buffer = BufferPool<BitmapDrawCall>.Allocate(text.Length)) {
                var layout = Font.LayoutString(text, buffer, scale: scale);
                var layoutSize = layout.Size;
                var position = new Vector2(Graphics.PreferredBackBufferWidth - (240 * scale), 30f).Floor();
                var dc = layout.DrawCalls;

                // fill quad + text quads
                LastPerformanceStatPrimCount = (layout.Count * 2) + 2;

                ir.FillRectangle(
                    Bounds.FromPositionAndSize(position, layoutSize),
                    Color.Black
                );
                ir.Layer += 1;
                ir.DrawMultiple(dc, position, material: Materials.ScreenSpaceBitmap, blendState: BlendState.AlphaBlend);
            }
        }
    }

    public abstract class Scene {
        internal SettingCollection Settings;

        public readonly TestGame Game;
        public readonly int Width, Height;

        public Scene (TestGame game, int width, int height) {
            Game = game;
            Width = width;
            Height = height;

            Settings = new SettingCollection(this);
        }

        public abstract void LoadContent ();
        public abstract void Draw (Frame frame);
        public abstract void Update (GameTime gameTime);

        public virtual void UIScene () {
        }

        internal bool KeyWasPressed (Keys key) {
            return Game.KeyboardState.IsKeyDown(key) && Game.PreviousKeyboardState.IsKeyUp(key);
        }
    }
}
