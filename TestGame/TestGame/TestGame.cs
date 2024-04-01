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
using Squared.PRGUI.Input;
using System.Text.RegularExpressions;
using Squared.Render.Resources;

namespace TestGame {
    public class TestGame : MultithreadedGame {
        public int? DefaultScene = null;

        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }
        public UIContext PRGUIContext;
        public IlluminantMaterials IlluminantMaterials;
        public ParticleMaterials ParticleMaterials;

        public Texture2DProvider TextureLoader { get; private set; }
        public FreeTypeFontProvider FontLoader { get; private set; }
        public EffectProvider EffectLoader { get; private set; }

        public KeyboardInputSource Keyboard = new KeyboardInputSource();
        public MouseInputSource Mouse = new MouseInputSource();
        public GamepadVirtualKeyboardAndCursor GamePad = new GamepadVirtualKeyboardAndCursor();

        public Material TextMaterial { get; private set; }
        public Material UnshadowedTextMaterial { get; private set; }

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
            typeof(Strokes),
            typeof(BitmapShaders),
            typeof(RasterShapeSpeed),
            typeof(SystemStress),
            typeof(HueTest),
            typeof(BitmapBillboards),
            typeof(ProjectorLight),
            typeof(GenerateMaps),
            typeof(JumpFlooding),
            typeof(SDFText),
            typeof(Emoji),
            typeof(HLSpritesHeight),
            typeof(HLSpritesSolve),
            typeof(VolumetricLight)
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
                TargetElapsedTime = TimeSpan.FromSeconds(1.0 / 2);

            Scenes = SceneTypes.Select(t => (Scene)Activator.CreateInstance(t, this, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight)).ToArray();
        }

        protected override void Initialize () {
            base.Initialize();

            Window.AllowUserResizing = false;
        }

        const float settingRowHeight = 26;

        private void UpdatePRGUI () {
            // TODO: Immediate mode
            PRGUIContext.CanvasSize = new Vector2(UIRenderTarget.Width, UIRenderTarget.Height);
            var window = PRGUIContext.Controls.OfType<Window>().FirstOrDefault();
            if (window == null) {
                window = new Window {
                    Title = "Settings",
                    Width = { Fixed = 500 },
                    Height = { Minimum = 200, Maximum = UIRenderTarget.Height - 200 },
                    AllowDrag = true,
                    AllowMaximize = false,
                    Appearance = { BackgroundColor = new Color(80, 80, 80) },
                    Alignment = new Vector2(0.99f, 0.9f),
                    ContainerFlags = ControlFlags.Container_Break_Auto | ControlFlags.Container_Row | ControlFlags.Container_Align_Start,
                    Scrollable = true,
                    ShowHorizontalScrollbar = false,
                    ClipChildren = true,
                    DynamicContents = BuildSettingsWindow,
                    Collapsible = true,
                };
                PRGUIContext.Controls.Add(window);
                // FIXME: This should work
                PRGUIContext.TrySetFocus(window, true);
            }

            var scene = Scenes[ActiveSceneIndex];
            var settings = scene.Settings;
            window.Data.Set(scene);

            PRGUIContext.UpdateInput(IsActive);
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

            scene.UIScene(ref builder);

            BuildGlobalSettings(ref builder);
            UpdatingSettings = false;
        }

        private void BuildGlobalSettings (ref ContainerBuilder builder) {
            var c = builder.Data<TitledContainer, string>("System")
                .SetTitle("System")
                .SetCollapsible(true)
                .Children();
            var apply = false;
            var vsync = Graphics.SynchronizeWithVerticalRetrace;
            var fullscreen = Graphics.IsFullScreen;
            c.Text<Checkbox>("VSync")
                .Value(ref vsync, out bool changed);
            if (changed) {
                Graphics.SynchronizeWithVerticalRetrace = vsync;
                apply = true;
            }
            c.Text<Checkbox>("Fullscreen")
                .Value(ref fullscreen, out changed);
            if (changed) {
                Graphics.IsFullScreen = fullscreen;
                apply = true;
            }

            if (apply)
                Graphics.ApplyChangesAfterPresent(RenderCoordinator);
        }

        private void RenderSettingGroup (KeyValuePair<string, SettingCollection.Group> kvp, ref ContainerBuilder builder) {
            var container = builder.Data<TitledContainer, string>(kvp.Key)
                .SetTitle(kvp.Key)
                .SetCollapsible(true)
                .AddContainerFlags(ControlFlags.Container_Break_Auto)
                .Children();

            bool smartBreakAllowed = false;
            foreach (var s in kvp.Value)
                RenderSetting(s, ref container, ref smartBreakAllowed);

            container.Finish();
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
                var label = builder.Text(name).SetLayoutFlags(breakFlags)
                    .SetWrap(false)
                    .SetMultiline(false);
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
                if (toggle.Key != default(Keys))
                    name = $"{toggle.Key} {name}";
                var control = builder.Text<Checkbox>(name)
                    .SetAutoSize(true)
                    .SetLayoutFlags(smartBreakFlags)
                    .SetValue(toggle.Value)
                    .Control;

                control.Data.Set<ISetting>(toggle);
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
                    control.Exponent = slider.Exponent;
                }

                control.Data.Set<ISetting>(slider);
            }
        }

        public KeyboardState KeyboardState => Keyboard.CurrentState;
        public MouseState MouseState => Mouse.CurrentState;

        public bool LeftMouse {
            get {
                return (Mouse.CurrentState.LeftButton == ButtonState.Pressed) && !IsMouseOverUI;
            }
        }

        public bool RightMouse {
            get {
                return (Mouse.CurrentState.RightButton == ButtonState.Pressed) && !IsMouseOverUI;
            }
        }

        protected override void OnLoadContent (bool isReloading) {
            RenderCoordinator.EnableThreading = false;

            TextureLoader = new Texture2DProvider(Assembly.GetExecutingAssembly(), RenderCoordinator) {
                DefaultOptions = new TextureLoadOptions {
                    Premultiply = true,
                    GenerateMips = true
                }
            };
            FontLoader = new FreeTypeFontProvider(Assembly.GetExecutingAssembly(), RenderCoordinator);
            EffectLoader = new EffectProvider(typeof(TestGame).Assembly, RenderCoordinator);

            Font = FontLoader.Load("FiraSans-Medium");
            Font.MipMapping = true; // FIXME: We're really blurry without this and I'm too lazy to fix it right now
            Font.SizePoints = 16f;
            Font.GlyphMargin = 2;

            Materials = new DefaultMaterialSet(RenderCoordinator);
            IlluminantMaterials = new IlluminantMaterials(RenderCoordinator, Materials);
            ParticleMaterials = new ParticleMaterials(Materials);
            RampTexture = TextureLoader.Load("light_ramp");

            TextMaterial = Materials.Get(Font.SDF ? Materials.DistanceFieldText : Materials.ShadowedBitmap, blendState: RenderStates.PorterDuffOver);
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.5f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One * 0.66f);
            if (!Font.SDF)
                TextMaterial.Parameters.ShadowMipBias.SetValue(1.5f);

            UnshadowedTextMaterial = TextMaterial.Clone();
            UnshadowedTextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0));

            UIRenderTarget = new AutoRenderTarget(
                RenderCoordinator,
                Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 1
            );

            var decorations = new DefaultDecorations(Materials, 3, 1) {
                DefaultFont = Font,
                TextMaterial = TextMaterial,
                ShadedTextMaterial = TextMaterial,
                // SelectedTextMaterial = TextMaterial
            };
            PRGUIContext = new UIContext(Materials, decorations) {
                InputSources = { Keyboard, Mouse, GamePad },
                AllowNullFocus = false
            };
            PRGUIContext.OnKeyEvent += PRGUIContext_OnKeyEvent;
            PRGUIContext.EventBus.Subscribe(null, UIEvents.CheckedChanged, PRGUI_OnCheckedChanged);
            PRGUIContext.EventBus.Subscribe(null, UIEvents.ValueChanged, PRGUI_OnValueChanged);

            LoadLUTs();

            /*
            foreach (var scene in Scenes)
                scene.LoadContent();
            */

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

        private bool PRGUIContext_OnKeyEvent (string name, Keys? key, char? ch) {
            if (name == UIEvents.KeyDown) {
                foreach (var setting in Scenes[_ActiveSceneIndex].Settings) {
                    var toggle = setting as Toggle;
                    if (toggle == null)
                        continue;
                    if (toggle.Key == key) {
                        toggle.Value = !toggle.Value;
                        return true;
                    }
                }
            }

            return false;
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
                window.Alignment = window.Alignment;
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
            Squared.Render.STB.ImageWrite.WriteImage(
                identity, File.OpenWrite("lut-identity.png"), Squared.Render.STB.ImageWriteFormat.PNG
            );
            */

            var names = TextureLoader.StreamSource.GetNames();
            foreach (var name in names) {
                if (name.StartsWith("LUTs\\"))
                    LoadLUT(name);
            }
        }

        Regex RowCountRE = new Regex(@"(\-x(?'count'[0-9]+))", RegexOptions.ExplicitCapture);

        private void LoadLUT (string name) {
            var key = Path.GetFileName(name);
            if (LUTs.ContainsKey(key))
                return;

            var texture = TextureLoader.Load(name, new TextureLoadOptions {
                Premultiply = false,
                GenerateMips = false,
                FloatingPoint = false,
                Enable16Bit = false,
                sRGBFromLinear = name.Contains("-linear"),
                sRGBToLinear = name.Contains("-srgb")
            });
            var rowCountM = RowCountRE.Match(name);
            var rowCount = rowCountM.Success
                ? int.Parse(rowCountM.Groups["count"]?.Value)
                : 1;
            
            var lut = new ColorLUT(texture, true, rowCount);
            LUTs.Add(key, lut);
        }

        protected override void Update (GameTime gameTime) {
            UpdatePRGUI();

            if (IsActive) {
                var ks = Keyboard.CurrentState;
                var pks = Keyboard.PreviousState;

                var alt = ks.IsKeyDown(Keys.LeftAlt) || ks.IsKeyDown(Keys.RightAlt);
                var wasAlt = pks.IsKeyDown(Keys.LeftAlt) || pks.IsKeyDown(Keys.RightAlt);

                if (ks.IsKeyDown(Keys.OemOpenBrackets) && !pks.IsKeyDown(Keys.OemOpenBrackets))
                    SetActiveScene(Arithmetic.Wrap(ActiveSceneIndex - 1, 0, Scenes.Length - 1));
                else if (ks.IsKeyDown(Keys.OemCloseBrackets) && !pks.IsKeyDown(Keys.OemCloseBrackets))
                    SetActiveScene(Arithmetic.Wrap(ActiveSceneIndex + 1, 0, Scenes.Length - 1));
                else if (ks.IsKeyDown(Keys.OemTilde) && !pks.IsKeyDown(Keys.OemTilde)) {
                    Graphics.SynchronizeWithVerticalRetrace = !Graphics.SynchronizeWithVerticalRetrace;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                } else if (ks.IsKeyDown(Keys.F10) && !pks.IsKeyDown(Keys.F10)) {
                    PRGUIContext.Engine?.SaveRecords(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "prgui.xml"));
                } else if (
                    (ks.IsKeyDown(Keys.Enter) && alt) &&
                    (!pks.IsKeyDown(Keys.Enter) || !wasAlt)
                ) {
                    Graphics.IsFullScreen = !Graphics.IsFullScreen;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                }
                else if (ks.IsKeyDown(Keys.OemPipe) && !pks.IsKeyDown(Keys.OemPipe)) {
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
                ir.DrawMultiple(dc, position, material: TextMaterial);
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

        protected void SetAO (List<LightSourceBase> lights, float opacity = 1, float radius = 0) {
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

        public virtual void UIScene (ref ContainerBuilder builder) {
        }

        internal bool KeyWasPressed (Keys key) {
            return Game.Keyboard.CurrentState.IsKeyDown(key) && 
                Game.Keyboard.PreviousState.IsKeyUp(key);
        }
    }
}
