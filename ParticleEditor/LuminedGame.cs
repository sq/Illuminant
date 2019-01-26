using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Modeling;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using Squared.Threading;
using ThreefoldTrials.Framework;
using Nuke = NuklearDotNet.Nuklear;

namespace Lumined {
    public partial class EditorGame : MultithreadedGame, INuklearHost {
        public Squared.Task.TaskScheduler Scheduler;
        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }
        public NuklearService Nuklear;
        public IlluminantMaterials IlluminantMaterials;
        public ParticleMaterials ParticleMaterials;
        public PropertyEditor UI;

        public EmbeddedTexture2DProvider TextureLoader { get; private set; }
        public EmbeddedFreeTypeFontProvider FontLoader { get; private set; }

        public KeyboardState PreviousKeyboardState, KeyboardState;
        public MouseState PreviousMouseState, MouseState;

        public Material TextMaterial { get; private set; }
        public Material WorldSpaceTextMaterial { get; private set; }
        public Material ScreenSpaceBezierVisualizer { get; private set; }

        public FreeTypeFont Font;
        public RenderTarget2D UIRenderTarget;

        private int LastPerformanceStatPrimCount = 0;

        public bool ShowPerformanceStats = false;
        public bool IsMouseOverUI = false;
        public long LastTimeOverUI;

        private bool DidLoadContent;

        public DisplayMode DesktopDisplayMode;
        public Pair<int> WindowedResolution;

        public EngineModel Model;
        public View View;
        public Controller Controller;

        private WaitCallback GCAfterVsync;

        internal TypedUniform<Squared.Illuminant.Uniforms.ClampedBezier4> uBezier;

        private GCHandle ControllerPin;
        public const float MinZoom = 0.25f, MaxZoom = 5.0f;
        public float Zoom = 1.0f;
        public Vector2 CameraPosition;
        public Vector2 CameraDragInitialPosition, CameraDragStart;

        private Future<ArraySegment<BitmapDrawCall>> LastReadbackResult = null;

        private long LastViewRelease = 0;

        public EditorGame () {
            // UniformBinding.ForceCompatibilityMode = true;

            GCAfterVsync = _GCAfterVsync;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.None;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = true;
            Graphics.IsFullScreen = false;

            Graphics.DeviceDisposing += Graphics_DeviceDisposing;
            Graphics.DeviceResetting += Graphics_DeviceResetting;

            Content.RootDirectory = "Content";

            UseThreadedDraw = true;
            IsFixedTimeStep = false;

            PreviousKeyboardState = Keyboard.GetState();
            IsMouseVisible = true;
            WindowedResolution = new Pair<int>(1920, 1080);

            Scheduler = new Squared.Task.TaskScheduler(Squared.Task.JobQueue.ThreadSafe);
        }

        private void ReleaseView () {
            lock (this) {
                LastViewRelease = Time.Ticks;
                if (View != null)
                    RenderCoordinator.DisposeResource(View);
                View = null;
            }
        }

        private void Graphics_DeviceDisposing (object sender, EventArgs e) {
            ReleaseView();
        }

        private void Graphics_DeviceResetting (object sender, EventArgs e) {
            ReleaseView();
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

        private void FreeContent () {
            Font.Dispose();
            Materials.Dispose();
            UIRenderTarget.Dispose();
        }

        protected override void Initialize () {
            base.Initialize();

            DesktopDisplayMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;

            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += Window_ClientSizeChanged;
        }

        protected override void UnloadContent () {
            RenderCoordinator.WaitForActiveDraws();

            FreeContent();

            base.UnloadContent();
        }

        protected override void OnExiting (object sender, EventArgs args) {
            Process.GetCurrentProcess().Kill();
        }

        protected override void LoadContent () {
            DidLoadContent = true;

            base.LoadContent();

            TextureLoader = new EmbeddedTexture2DProvider(RenderCoordinator) {
                DefaultOptions = new TextureLoadOptions {
                    Premultiply = true,
                    GenerateMips = true
                }
            };
            FontLoader = new EmbeddedFreeTypeFontProvider(RenderCoordinator);

            Font = FontLoader.Load("Lato-Regular");
            Font.GlyphMargin = 2;
            Font.Gamma = 0.8f;

            Materials = new DefaultMaterialSet(RenderCoordinator);
            IlluminantMaterials = new IlluminantMaterials(Materials);
            ParticleMaterials = new ParticleMaterials(Materials);

            if (UI == null)
                UI = new PropertyEditor(this);

            TextMaterial = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.6f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One * 0.75f);

            WorldSpaceTextMaterial = Materials.Get(Materials.WorldSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            WorldSpaceTextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.9f));
            WorldSpaceTextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One * 0.75f);

            // FIXME: Memory leak
            var eep = new EmbeddedEffectProvider(typeof(Squared.Illuminant.Particles.ParticleSystem).Assembly, RenderCoordinator);
            ScreenSpaceBezierVisualizer = new Material(eep.Load("VisualizeBezier"), "ScreenSpaceBezierVisualizer");
            Materials.Add(ScreenSpaceBezierVisualizer);
            ScreenSpaceBezierVisualizer = Materials.Get(ScreenSpaceBezierVisualizer, blendState: BlendState.AlphaBlend);

            uBezier = Materials.NewTypedUniform<Squared.Illuminant.Uniforms.ClampedBezier4>("Bezier");
            
            UIRenderTarget = new RenderTarget2D(
                GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                false, SurfaceFormat.Color, DepthFormat.None, 1, RenderTargetUsage.PlatformContents
            );

            if (Nuklear == null)
                Nuklear = new NuklearService(this) {
                    Font = Font,
                    Scene = UI.UIScene
                };
            else
                Nuklear.Font = Font;

            Nuklear.VerticalPadding = 3;

            LastTimeOverUI = Time.Ticks;

            if (Model == null)
                Model = CreateNewModel();

            if (View != null)
                View.Dispose();

            CreateView();
        }

        private void CreateView () {
            View = new View(Model);

            if (Controller == null) {
                Controller = new Controller(this, Model, View);
                ControllerPin = GCHandle.Alloc(Controller, GCHandleType.Normal);
                if (Model.Systems.Count == 0)
                    Controller.AddSystem();
            } else
                Controller.View = View;

            View.Initialize(this);
        }

        public EngineModel CreateNewModel () {
            var result = new EngineModel {
                UserData = {
                    { "EditorData", new EditorData() }
                }
            };
            return result;
        }

        private void Window_ClientSizeChanged (object sender, EventArgs e) {
            Console.WriteLine("ClientSizeChanged");
            if (Window.ClientBounds.Width <= 0)
                return;

            RenderCoordinator.WaitForActiveDraws();
            ReleaseView();

            if (!Graphics.IsFullScreen) {
                WindowedResolution = new Pair<int>(Window.ClientBounds.Width, Window.ClientBounds.Height);
                Graphics.PreferredBackBufferWidth = Window.ClientBounds.Width;
                Graphics.PreferredBackBufferHeight = Window.ClientBounds.Height;
            }
            Graphics.ApplyChangesAfterPresent(RenderCoordinator);
            RenderCoordinator.AfterPresent(() => {
                Materials.AutoSetViewTransform();
                RenderCoordinator.DisposeResource(UIRenderTarget);
                UIRenderTarget = new RenderTarget2D(
                    GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                    false, SurfaceFormat.Color, DepthFormat.None, 1, RenderTargetUsage.PlatformContents
                );
            });
        }

        protected override void Update (GameTime gameTime) {
            if (View == null) {
                if ((Time.Ticks - LastViewRelease) > Time.MillisecondInTicks * 1000)
                    CreateView();
            }

            using (UI.KeyboardInputHandler.Deactivate())
                Scheduler.Step();

            PreviousKeyboardState = KeyboardState;
            PreviousMouseState = MouseState;
            KeyboardState = Keyboard.GetState();
            MouseState = Mouse.GetState();

            if (IsActive) {
                if (!String.IsNullOrWhiteSpace(Model.Filename))
                    Window.Title = "LuminEd - " + Path.GetFileName(Model.Filename);
                else
                    Window.Title = "LuminEd - untitled";

                var alt = KeyboardState.IsKeyDown(Keys.LeftAlt) || KeyboardState.IsKeyDown(Keys.RightAlt);
                var wasAlt = PreviousKeyboardState.IsKeyDown(Keys.LeftAlt) || PreviousKeyboardState.IsKeyDown(Keys.RightAlt);

                var isDraggingCamera = MouseState.MiddleButton == ButtonState.Pressed;
                var wasDraggingCamera = PreviousMouseState.MiddleButton == ButtonState.Pressed;

                if (isDraggingCamera) {
                    if (wasDraggingCamera) {
                        var delta = new Vector2(MouseState.X, MouseState.Y) - CameraDragStart;
                        CameraPosition = CameraDragInitialPosition - (delta / Zoom);
                    } else {
                        CameraDragInitialPosition = CameraPosition;
                        CameraDragStart = new Vector2(MouseState.X, MouseState.Y);
                    }
                }
                
                if (KeyboardState.IsKeyDown(Keys.OemTilde) && !PreviousKeyboardState.IsKeyDown(Keys.OemTilde)) {
                    Graphics.SynchronizeWithVerticalRetrace = !Graphics.SynchronizeWithVerticalRetrace;
                    Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                }
                else if (
                    (KeyboardState.IsKeyDown(Keys.Enter) && alt) &&
                    (!PreviousKeyboardState.IsKeyDown(Keys.Enter) || !wasAlt)
                ) {
                    SetFullScreen(!Graphics.IsFullScreen);
                }
                else if (KeyboardState.IsKeyDown(Keys.OemPipe) && !PreviousKeyboardState.IsKeyDown(Keys.OemPipe)) {
                    UniformBinding.ForceCompatibilityMode = !UniformBinding.ForceCompatibilityMode;
                }
            }
            
            PerformanceStats.Record(this);

            base.Update(gameTime);
        }

        public void SetFullScreen (bool state) {
            Graphics.IsFullScreen = state;
            if (state) {
                Graphics.PreferredBackBufferWidth = DesktopDisplayMode.Width;
                Graphics.PreferredBackBufferHeight = DesktopDisplayMode.Height;
            } else {
                Graphics.PreferredBackBufferWidth = WindowedResolution.First;
                Graphics.PreferredBackBufferHeight = WindowedResolution.Second;
            }
            Graphics.ApplyChangesAfterPresent(RenderCoordinator);
        }

        public Vector2 ViewOffset {
            get {
                var result = new Vector2(
                    -(Graphics.PreferredBackBufferWidth - UI.SidePanelWidth) / 2f,
                    -Graphics.PreferredBackBufferHeight / 2f
                ) / Zoom;

                return result + CameraPosition;
            }
        }

        public override void Draw (GameTime gameTime, Frame frame) {
            if ((Window.ClientBounds.Width != Graphics.PreferredBackBufferWidth) || (Window.ClientBounds.Height != Graphics.PreferredBackBufferHeight))
                return;

            var newSize = Graphics.PreferredBackBufferWidth > 2000 ? 15f : 13.5f;
            if (Font.SizePoints != newSize) {
                Font.SizePoints = newSize;
                Nuklear.InvalidateFontCache();
            }

            View view;
            lock (this)
                view = View;

            Nuklear.SceneBounds = Bounds.FromPositionAndSize(0, 0, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight);
            Nuklear.UpdateInput(
                PreviousMouseState, MouseState, 
                PreviousKeyboardState, KeyboardState, 
                IsMouseOverUI, UI.KeyboardInputHandler.Buffer
            );

            UI.KeyboardInputHandler.Buffer.Clear();

            using (UI.KeyboardInputHandler.Deactivate())
            using (var group = BatchGroup.ForRenderTarget(frame, -9990, UIRenderTarget)) {
                ClearBatch.AddNew(group, -1, Materials.Clear, clearColor: Color.Transparent);
                Nuklear.Render(gameTime.ElapsedGameTime.Seconds, group, 1);
            }

            Controller.Update();

            if (View != null) {
                MaybeDrawPreviousBitmaps(frame, 3);
                View.Update(this, frame, -3, gameTime.ElapsedGameTime.Ticks);
                ClearBatch.AddNew(frame, -2, Materials.Clear, View.GetData().BackgroundColor);
            } else {
                ClearBatch.AddNew(frame, -2, Materials.Clear, Color.Black);
            }

            var ir = new ImperativeRenderer(
                frame, Materials, 
                blendState: BlendState.AlphaBlend,
                samplerState: SamplerState.LinearClamp,
                worldSpace: false,
                layer: 9999
            );
            
            Materials.ViewportPosition = ViewOffset;
            Materials.ViewportScale = Zoom * Vector2.One;

            if (View != null) {
                var bg = View.GetData().Background;
                if (bg != null) {
                    bg.EnsureInitialized(View.Engine.Configuration.TextureLoader);
                    if (bg.IsInitialized)
                        ir.Draw(bg.Instance, Vector2.Zero, origin: Vector2.One * 0.5f, layer: -1, worldSpace: true);
                }

                foreach (var spr in View.GetData().Sprites) {
                    if (spr.Texture == null)
                        continue;
                    spr.Texture.EnsureInitialized(View.Engine.Configuration.TextureLoader);
                    if (!spr.Texture.IsInitialized)
                        continue;
                    var tex = spr.Texture.Instance;
                    var loc = spr.Location.Evaluate((float)View.Time.Seconds, View.Engine.ResolveVector3);
                    var tlPx = spr.TextureTopLeftPx.GetValueOrDefault(Vector2.Zero);
                    var szPx = spr.TextureSizePx.GetValueOrDefault(new Vector2(tex.Width, tex.Height));
                    var dc = new BitmapDrawCall(
                        tex, new Vector2(loc.X, loc.Y),
                        Bounds.FromPositionAndSize(
                            tlPx / new Vector2(tex.Width, tex.Height),
                            szPx / new Vector2(tex.Width, tex.Height)
                        ), Color.White, Vector2.One * spr.Scale, Vector2.One * 0.5f
                    );
                    ir.Draw(dc, layer: 0, worldSpace: true);
                }
            }

            var elapsedSeconds = TimeSpan.FromTicks(Time.Ticks - LastTimeOverUI).TotalSeconds;
            float uiOpacity = Arithmetic.Lerp(1.0f, 0.4f, (float)((elapsedSeconds - 0.66) * 2.25f));

            ir.Draw(UIRenderTarget, Vector2.Zero, multiplyColor: Color.White * uiOpacity);

            if (View != null) {
                if (!View.GetData().DrawAsBitmaps)
                    View.Draw(this, frame, 3);
            }

            Controller.Draw(this, frame, 4);

            if (ShowPerformanceStats)
                DrawPerformanceStats(ref ir);

            ThreadPool.QueueUserWorkItem(GCAfterVsync, null);
        }

        private void MaybeDrawPreviousBitmaps (IBatchContainer container, int layer) {
            if (!View.GetData().DrawAsBitmaps)
                return;

            var ur = View.UpdateResults;
            if (ur == null)
                return;

            DrawPreviousBitmapsAsync(container, layer, ur);
        }

        private async System.Threading.Tasks.Task DrawPreviousBitmapsAsync (
            IBatchContainer container, int layer, 
            Squared.Illuminant.Particles.ParticleSystem.UpdateResult[] results
        ) {
            var batches = new List<BitmapBatch>(results.Length);
            for (int i = 0; i < results.Length; i++) {
                var ur = results[i];
                var batch = BitmapBatch.New(
                    container, layer,
                    Materials.GetBitmapMaterial(true, blendState: BlendState.AlphaBlend),
                    samplerState: (ur.System.Configuration.Appearance?.Bilinear ?? true)
                        ? SamplerState.LinearClamp
                        : SamplerState.PointClamp
                );
                batch.Suspend();
                batches.Add(batch);
            }

            var futures = new List<Future<ArraySegment<BitmapDrawCall>>>(results.Length);
            foreach (var ur in results)
                futures.Add(ur.ReadbackResult);

            for (int i = 0; i < results.Length; i++) {
                var f = futures[i];
                await f;
                var batch = batches[i];
                batch.AddRange(f.Result);
                batch.Dispose();
            }
        }

        private void _GCAfterVsync (object _) {
            // Attempt to start a GC after we've issued all rendering commands to the GPU.
            // This should hide most or all of the GC time behind the rendering time.
            if (!Graphics.SynchronizeWithVerticalRetrace)
                return;
            if (RenderCoordinator.TryWaitForPresentToStart(3, 1))
                GC.Collect(1, GCCollectionMode.Optimized);
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
}