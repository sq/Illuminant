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
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Game;
using Squared.Illuminant;
using Squared.PRGUI;
using Squared.PRGUI.Controls;
using Squared.PRGUI.Decorations;
using Squared.PRGUI.Layout;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Util.Event;
using TestGame.Scenes;
using ThreefoldTrials.Framework;
using PRGUISlider = Squared.PRGUI.Controls.Slider;
using PRGUIDropdown = Squared.PRGUI.Controls.Dropdown<object>;
using Squared.PRGUI.Imperative;

namespace TestGame {
    public class TestGame : MultithreadedGame {
        public int? DefaultScene = null;

        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }
        public UIContext PRGUIContext;
        public IlluminantMaterials IlluminantMaterials;
        public ParticleMaterials ParticleMaterials;

        public EmbeddedTexture2DProvider TextureLoader { get; private set; }
        public EmbeddedFreeTypeFontProvider FontLoader { get; private set; }

        internal KeyboardInput KeyboardInputHandler;
        public KeyboardState PreviousKeyboardState, KeyboardState;
        public MouseState PreviousMouseState, MouseState;

        public Material TextMaterial { get; private set; }

        public FreeTypeFont Font;
        public Texture2D RampTexture;
        public AutoRenderTarget UIRenderTarget;

        public readonly Scene[] Scenes;
        private int _ActiveSceneIndex;

        public int ActiveSceneIndex {
            get {
                return _ActiveSceneIndex;
            }
        }

        private int LastPerformanceStatPrimCount = 0;

        public bool IsMouseOverUI = false, TearingTest = false;
        public long LastTimeOverUI;
        bool UpdatingSettings;

        public readonly Dictionary<string, ColorLUT> LUTs = 
            new Dictionary<string, ColorLUT>(StringComparer.OrdinalIgnoreCase);

        public static readonly Type[] SceneTypes = new[] {
            typeof(HeightVolumeTest),
            typeof(TwoPointFiveDTest),
            typeof(SC3),
            typeof(DistanceFieldEditor),
            typeof(ScrollingGeo),
            typeof(SimpleParticles),
            typeof(LightProbeTest),
            typeof(ParticleLights),
            typeof(DynamicObstructions),
            typeof(DitheringTest),
            typeof(LineLight),
            typeof(VectorFieldTest),
            typeof(LUTTest),
#if compiled_model
            typeof(LoadCompiledModel),
#endif
            typeof(Shapes),
            typeof(SystemStress),
            typeof(PaletteTest),
            typeof(HueTest),
            typeof(BitmapShaders),
            typeof(RasterShapeSpeed),
            typeof(BitmapBillboards),
            typeof(ProjectorLight)
        };

        public TestGame () {
            // UniformBinding.ForceCompatibilityMode = true;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = false;
            Graphics.IsFullScreen = false;

            Content.RootDirectory = "Content";

            IsFixedTimeStep = false;

            if (IsFixedTimeStep)
                TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 30);

            PreviousKeyboardState = Keyboard.GetState();

            Scenes = SceneTypes.Select(t => (Scene)Activator.CreateInstance(t, this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight)).ToArray();

            KeyboardInputHandler = new KeyboardInput();
            KeyboardInputHandler.Install();
        }

        protected override void Initialize () {
            base.Initialize();

            Window.AllowUserResizing = false;
        }

        const float settingRowHeight = 26;

        /*
        private NString Other;

        protected unsafe void UIScene () {
            var ctx = Nuklear.Context;

            var scene = Scenes[ActiveSceneIndex];
            var settings = scene.Settings;

            var isWindowOpen = Nuke.nk_begin(
                ctx, "Settings", new NuklearDotNet.NkRect(Graphics.PreferredBackBufferWidth - 508, Graphics.PreferredBackBufferHeight - 758, 500, 750),
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
                        Nuklear.NewRow(3);
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

        FIXME: Global settings
        NString sSystem = new NString("System");
        */

        private Dictionary<string, Container> SettingGroups = new Dictionary<string, Container>();
        private Dictionary<ISetting, Control> SettingControls = new Dictionary<ISetting, Control>();

        private void UpdatePRGUI () {
            // TODO: Immediate mode
            PRGUIContext.CanvasSize = new Vector2(UIRenderTarget.Width, UIRenderTarget.Height);
            var window = PRGUIContext.Controls.Cast<Window>().FirstOrDefault();
            if (window == null) {
                window = new Window {
                    Title = "Settings",
                    FixedWidth = 500,
                    MinimumHeight = 200,
                    MaximumHeight = UIRenderTarget.Height - 200,
                    AllowDrag = true,
                    AllowMaximize = false,
                    BackgroundColor = new Color(80, 80, 80),
                    ScreenAlignment = new Vector2(0.99f, 0.9f),
                    ContainerFlags = ControlFlags.Container_Wrap | ControlFlags.Container_Row | ControlFlags.Container_Align_Start,
                    Scrollable = true,
                    ShowHorizontalScrollbar = false,
                    ClipChildren = true,
                    DynamicContents = BuildSettingsWindow
                };
                PRGUIContext.Controls.Add(window);
                // FIXME: This should work
                PRGUIContext.TrySetFocus(window, true);
            }

            var scene = Scenes[ActiveSceneIndex];
            var settings = scene.Settings;

            if (scene != window.Data.Get<Scene>()) {
                window.Data.Set(scene);
                SettingGroups.Clear();
                SettingControls.Clear();
            }
        }

        private void BuildSettingsWindow (ref ContainerBuilder builder) {
            UpdatingSettings = true;
            var scene = Scenes[ActiveSceneIndex];
            var settings = scene.Settings;

            bool smartBreakAllowed = false;
            foreach (var s in settings)
                RenderSetting(s, ref builder, ref smartBreakAllowed);

            foreach (var kvp in settings.Groups.OrderBy(kvp => kvp.Key))
                RenderSettingGroup(kvp, ref builder);

            BuildGlobalSettings(ref builder);
            UpdatingSettings = false;
        }

        private void BuildGlobalSettings (ref ContainerBuilder builder) {
            var c = builder.Data<TitledContainer, string>("System")
                .SetTitle("System")
                .SetCollapsible(true)
                .Children();
            c.Text<Checkbox>("VSync")
                .SetValue(Graphics.SynchronizeWithVerticalRetrace);
            c.Text<Checkbox>("Fullscreen")
                .SetValue(Graphics.IsFullScreen);
            c.Text<Checkbox>("Tearing Test")
                .SetValue(TearingTest);
        }

        private void RenderSettingGroup (KeyValuePair<string, SettingCollection.Group> kvp, ref ContainerBuilder builder) {
            var container = builder.Data<TitledContainer, string>(kvp.Key)
                .SetTitle(kvp.Key)
                .SetCollapsible(true)
                .Children();

            bool smartBreakAllowed = false;
            foreach (var s in kvp.Value)
                RenderSetting(s, ref container, ref smartBreakAllowed);
        }

        protected void RenderSetting (ISetting s, ref ContainerBuilder builder, ref bool smartBreakAllowed) {
            var name = s.Name;
            var dropdown = s as IDropdown;
            var toggle = s as Toggle;
            var slider = s as Slider;
            var breakFlags = ControlFlags.Layout_ForceBreak | ControlFlags.Layout_Fill_Row;

            var smartBreakFlags = breakFlags;
            if (smartBreakAllowed)
                smartBreakFlags = ControlFlags.Layout_Fill_Row;

            if (dropdown != null) {
                smartBreakAllowed = false;
                var label = builder.Text(name).SetLayoutFlags(breakFlags);
                var control = builder.New<PRGUIDropdown>()
                    .SetAutoSize(false, true)
                    .SetTextAlignment(HorizontalAlignment.Left)
                    .Control;
                label.SetFocusBeneficiary(control);

                if (control.Data.Get<ISetting>() != dropdown) {
                    control.Items.Clear();
                    for (var i = 0; i < dropdown.Count; i++)
                        control.Items.Add(dropdown.GetItem(i));
                    control.Data.Set<ISetting>(dropdown);
                }

                if (control != PRGUIContext.Focused)
                    control.SelectedItem = dropdown.GetItem(dropdown.SelectedIndex);
            } else if (toggle != null) {
                smartBreakAllowed = true;
                var control = builder.Text<Checkbox>(name)
                    .SetAutoSize(true)
                    .SetLayoutFlags(smartBreakFlags)
                    .SetValue(toggle.Value);
            } else if (slider != null) {
                smartBreakAllowed = false;
                var control = builder.New<ParameterEditor<double>>()
                    .SetLayoutFlags(breakFlags)
                    .Control;

                if (control != PRGUIContext.Focused) {
                    control.HorizontalAlignment = HorizontalAlignment.Right;
                    control.IntegerOnly = slider.Integral;
                    control.DoubleOnly = !slider.Integral;
                    if (slider.Integral)
                        control.ValueFilter = (d) => Math.Round(d, 0, MidpointRounding.AwayFromZero);
                    else
                        control.ValueFilter = (d) => Math.Round(d, 3, MidpointRounding.AwayFromZero);
                    control.Minimum = slider.Min;
                    control.Maximum = slider.Max;
                    control.Value = slider.Value;
                    control.Increment = slider.Speed;
                    control.Description = name;
                    control.Exponential = slider.Exponential;
                }
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

        protected override void OnLoadContent (bool isReloading) {
            RenderCoordinator.EnableThreading = false;

            TextureLoader = new EmbeddedTexture2DProvider(RenderCoordinator) {
                DefaultOptions = new TextureLoadOptions {
                    Premultiply = true,
                    GenerateMips = true
                }
            };
            FontLoader = new EmbeddedFreeTypeFontProvider(RenderCoordinator);

            Font = FontLoader.Load("FiraSans-Medium");
            Font.MipMapping = true; // FIXME: We're really blurry without this and I'm too lazy to fix it right now
            Font.sRGB = true;
            Font.SizePoints = 16f;
            Font.GlyphMargin = 2;

            Materials = new DefaultMaterialSet(RenderCoordinator);
            IlluminantMaterials = new IlluminantMaterials(Materials);
            IlluminantMaterials.Load(RenderCoordinator);
            ParticleMaterials = new ParticleMaterials(Materials);
            RampTexture = TextureLoader.Load("light_ramp");

            TextMaterial = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.5f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One * 0.66f);
            TextMaterial.Parameters.ShadowMipBias.SetValue(1.5f);

            UIRenderTarget = new AutoRenderTarget(
                RenderCoordinator,
                Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 1
            );

            PRGUIContext = new UIContext(Materials, new DefaultDecorations(3, 1) { DefaultFont = Font });
            PRGUIContext.EventBus.Subscribe(null, UIEvents.CheckedChanged, PRGUI_OnCheckedChanged);
            PRGUIContext.EventBus.Subscribe(null, UIEvents.ValueChanged, PRGUI_OnValueChanged);

            LoadLUTs();

            /*
            foreach (var scene in Scenes)
                scene.LoadContent();
            */

            LastTimeOverUI = Time.Ticks;

            _ActiveSceneIndex = -1;

            var sceneNameArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--scene:"));
            if (sceneNameArg != null) {
                sceneNameArg = sceneNameArg.Substring(sceneNameArg.IndexOf(":") + 1);
                foreach (var s in Scenes)
                    if (s.GetType().Name.Equals(sceneNameArg, StringComparison.OrdinalIgnoreCase)) {
                        SetActiveScene(Array.IndexOf(Scenes, s));
                        return;
                    }

                throw new Exception("Could not find a scene with the name " + sceneNameArg);
            } else {
                SetActiveScene(DefaultScene ?? Scenes.Length - 1);
            }
        }

        private void PRGUI_OnCheckedChanged (IEventInfo ei) {
            if (UpdatingSettings)
                return;

            var c = (Checkbox)ei.Source;
            var s = c.Data.Get<ISetting>(null, null);
            if (s == null)
                return;
            var t = (Toggle)s;
            t.Value = c.Checked;
        }

        private void PRGUI_OnValueChanged (IEventInfo ei) {
            if (UpdatingSettings)
                return;

            var ctl = (Control)ei.Source;
            var s = ctl.Data.Get<ISetting>(null, null);
            if (s == null)
                return;

            var prsl = (ctl as PRGUISlider);
            var prd = (ctl as PRGUIDropdown);
            var prp = (ctl as ParameterEditor<double>);
            if (prsl != null)
                ((Slider)s).Value = prsl.Value;
            else if (prd != null)
                ((IDropdown)s).SelectedItem = prd.SelectedItem;
            else if (prp != null)
                ((Slider)s).Value = (float)prp.Value;
        }

        protected override void OnUnloadContent () {
            Process.GetCurrentProcess().Kill();
            Environment.Exit(0);
        }

        private void SetActiveScene (int index) {
            RenderCoordinator.WaitForActiveDraws();

            Scene oldScene = null;
            if (_ActiveSceneIndex >= 0)
                oldScene = Scenes[_ActiveSceneIndex];

            _ActiveSceneIndex = index;

            var newScene = Scenes[index];
            if (oldScene != newScene) {
                if (oldScene != null)
                    oldScene.DoUnloadContent();
                newScene.DoLoadContent();
            }

            var window = PRGUIContext.Controls.OfType<Window>().FirstOrDefault();
            if (window != null)
                window.ScreenAlignment = window.ScreenAlignment;
        }

        private void LoadLUTs () {
            var identityF = ColorLUT.CreateIdentity(
                RenderCoordinator, LUTPrecision.Float32, LUTResolution.High, false
            );
            var identity = ColorLUT.CreateIdentity(
                RenderCoordinator, LUTPrecision.UInt16, LUTResolution.High, false
            );
            LUTs.Add("Identity", identity);

            /*
            Squared.Render.STB.ImageWrite.WriteImage(
                identityF, File.OpenWrite("lut-identity.hdr"), Squared.Render.STB.ImageWriteFormat.HDR
            );
#if !FNA
            identityF.Texture.SaveAsPng(File.OpenWrite("lut-identity.png"), identityF.Texture.Width, identityF.Texture.Height);
#endif
            */

            var names = TextureLoader.GetNames("LUTs\\");
            foreach (var name in names)
                LoadLUT(name);
        }

        private void LoadLUT (string name) {
            var key = Path.GetFileName(name);
            if (LUTs.ContainsKey(key))
                return;

            var texture = TextureLoader.Load(name, new TextureLoadOptions {
                Premultiply = false,
                GenerateMips = false,
                FloatingPoint = true
            });
            var lut = new ColorLUT(texture, true);
            LUTs.Add(key, lut);
        }

        protected override void Update (GameTime gameTime) {
            PreviousKeyboardState = KeyboardState;
            PreviousMouseState = MouseState;
            KeyboardState = Keyboard.GetState();
            MouseState = Mouse.GetState();

            UpdatePRGUI();

            if (IsActive) {
                PRGUIContext.UpdateInput(MouseState, KeyboardState);
                var alt = KeyboardState.IsKeyDown(Keys.LeftAlt) || KeyboardState.IsKeyDown(Keys.RightAlt);
                var wasAlt = PreviousKeyboardState.IsKeyDown(Keys.LeftAlt) || PreviousKeyboardState.IsKeyDown(Keys.RightAlt);

                if (KeyboardState.IsKeyDown(Keys.OemOpenBrackets) && !PreviousKeyboardState.IsKeyDown(Keys.OemOpenBrackets))
                    SetActiveScene(Arithmetic.Wrap(ActiveSceneIndex - 1, 0, Scenes.Length - 1));
                else if (KeyboardState.IsKeyDown(Keys.OemCloseBrackets) && !PreviousKeyboardState.IsKeyDown(Keys.OemCloseBrackets))
                    SetActiveScene(Arithmetic.Wrap(ActiveSceneIndex + 1, 0, Scenes.Length - 1));
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

            PRGUIContext.Update();
            IsMouseOverUI = PRGUIContext.IsActive;
            if (IsMouseOverUI)
                LastTimeOverUI = Time.Ticks;

            PerformanceStats.Record(this);

            Window.Title = String.Format("Scene {0}: {1}", ActiveSceneIndex, scene.GetType().Name);

            base.Update(gameTime);
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            // Nuklear.UpdateInput(IsActive, PreviousMouseState, MouseState, PreviousKeyboardState, KeyboardState, IsMouseOverUI, KeyboardInputHandler.Buffer);

            KeyboardInputHandler.Buffer.Clear();

            PRGUIContext.Rasterize(frame, UIRenderTarget, -9900);

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

            if (TearingTest) {
                var x = (Time.Ticks / 20000) % Graphics.PreferredBackBufferWidth;
                ir.FillRectangle(Bounds.FromPositionAndSize(
                    x, 0, 6, Graphics.PreferredBackBufferHeight
                ), Color.Red);
            }
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

        private bool HasLoadedContent;

        public void DoLoadContent () {
            LoadContent();
            HasLoadedContent = true;
        }

        public void DoUnloadContent () {
            if (HasLoadedContent)
                UnloadContent();
            HasLoadedContent = false;
        }

        protected void SetAO (List<LightSource> lights, float opacity = 1, float radius = 0) {
            foreach (var light in lights) {
                var dl = light as DirectionalLightSource;
                var sl = light as SphereLightSource;
                if (dl != null) {
                    dl.AmbientOcclusionOpacity = opacity;
                    dl.AmbientOcclusionRadius = radius;
                }
                if (sl != null) {
                    sl.AmbientOcclusionOpacity = opacity;
                    sl.AmbientOcclusionRadius = radius;
                }
            }
        }

        public abstract void LoadContent ();
        public abstract void UnloadContent ();
        public abstract void Draw (Frame frame);
        public abstract void Update (GameTime gameTime);

        public virtual void UIScene () {
        }

        internal bool KeyWasPressed (Keys key) {
            return Game.KeyboardState.IsKeyDown(key) && Game.PreviousKeyboardState.IsKeyUp(key);
        }
    }
}
