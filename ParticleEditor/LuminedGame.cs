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
using Squared.Render.Resources;

namespace Lumined {
    public partial class EditorGame : MultithreadedGame, INuklearHost {
        private readonly DepthStencilState SpriteDSS = new DepthStencilState {
            DepthBufferEnable = true,
            DepthBufferWriteEnable = true,
            DepthBufferFunction = CompareFunction.Always
        };

        public Squared.Task.TaskScheduler Scheduler;
        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }
        public NuklearService Nuklear;
        public IlluminantMaterials IlluminantMaterials;
        public ParticleMaterials ParticleMaterials;
        public LightingRenderer LightingRenderer;
        public PropertyEditor UI;

        public Texture2DProvider TextureLoader { get; private set; }
        public FreeTypeFontProvider FontLoader { get; private set; }

        public KeyboardState PreviousKeyboardState, KeyboardState;
        public MouseState PreviousMouseState, MouseState;

        public Material TextMaterial { get; private set; }
        public Material WorldSpaceTextMaterial { get; private set; }
        public Material ScreenSpaceBezierVisualizer { get; private set; }

        public FreeTypeFont Font;
        public AutoRenderTarget UIRenderTarget;

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

        private long ResizeStartedWhen = 0;
        private long ResizeSettleTime = Time.MillisecondInTicks * 300;
        private bool WasResized;

        private Future<ArraySegment<BitmapDrawCall>> LastReadbackResult = null;

        private long LastViewRelease = 0;

        private bool IsFirstFrame = true;

        public EditorGame () {
            // UniformBinding.ForceCompatibilityMode = true;

            GCAfterVsync = _GCAfterVsync;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = false;
            Graphics.IsFullScreen = false;

            Graphics.DeviceDisposing += Graphics_DeviceDisposing;
            Graphics.DeviceResetting += Graphics_DeviceResetting;

            Content.RootDirectory = "Content";

            IsFixedTimeStep = false;

            PreviousKeyboardState = Keyboard.GetState();
            IsMouseVisible = true;
            WindowedResolution = new Pair<int>(1920, 1080);

            Scheduler = new Squared.Task.TaskScheduler(Squared.Task.JobQueue.ThreadSafe);
        }

        private bool IsResizing {
            get {
                var elapsed = Time.Ticks - ResizeStartedWhen;
                return elapsed <= ResizeSettleTime;
            }
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

#if FNA
            Window.AllowUserResizing = true;
#else
            Window.AllowUserResizing = false;
#endif
            Window.ClientSizeChanged += Window_ClientSizeChanged;
        }

        protected override void OnUnloadContent () {
            Process.GetCurrentProcess().Kill();
            Environment.Exit(0);

            FreeContent();
        }

        protected override void OnExiting (object sender, EventArgs args) {
            Process.GetCurrentProcess().Kill();
        }

        protected override void OnLoadContent (bool isReloading) {
            DidLoadContent = true;

            TextureLoader = new Texture2DProvider(Assembly.GetExecutingAssembly(), RenderCoordinator) {
                DefaultOptions = new TextureLoadOptions {
                    Premultiply = true,
                    GenerateMips = true
                }
            };
            FontLoader = new FreeTypeFontProvider(Assembly.GetExecutingAssembly(), RenderCoordinator);

            Font = FontLoader.Load("Lato-Regular");
            Font.GlyphMargin = 2;
            Font.Gamma = 0.8f;

            Materials = new DefaultMaterialSet(RenderCoordinator);
            IlluminantMaterials = new IlluminantMaterials(RenderCoordinator, Materials);
            ParticleMaterials = new ParticleMaterials(Materials);
            LightingRenderer = new LightingRenderer(
                Content, RenderCoordinator, Materials, 
                new LightingEnvironment(), new RendererConfiguration(4000, 4000, false) {
                    /*
                    EnableGBuffer = true,
                    GBufferViewportRelative = true,
                    TwoPointFiveD = true
                    */
                }, 
                IlluminantMaterials
            );
            // LightingRenderer.DistanceField = new DistanceField(RenderCoordinator, 1024, 1024, 128, 8, 0.25f);

            if (UI == null)
                UI = new PropertyEditor(this);

            TextMaterial = Materials.Get(Materials.ShadowedBitmap, blendState: BlendState.AlphaBlend);
            TextMaterial.DefaultParameters.Add("GlobalShadowColor", new Vector4(0, 0, 0, 0.6f));
            TextMaterial.DefaultParameters.Add("ShadowOffset", Vector2.One * 0.75f);

            WorldSpaceTextMaterial = Materials.Get(Materials.ShadowedBitmap, blendState: BlendState.AlphaBlend);
            WorldSpaceTextMaterial.DefaultParameters.Add("GlobalShadowColor", new Vector4(0, 0, 0, 0.9f));
            WorldSpaceTextMaterial.DefaultParameters.Add("ShadowOffset", Vector2.One * 0.75f);

            // FIXME: Memory leak
            var eep = new EffectProvider(typeof(Squared.Illuminant.Particles.ParticleSystem).Assembly, RenderCoordinator);
            ScreenSpaceBezierVisualizer = new Material(eep.Load("VisualizeBezier"), "ScreenSpaceBezierVisualizer");
            Materials.Add(ScreenSpaceBezierVisualizer);
            ScreenSpaceBezierVisualizer = Materials.Get(ScreenSpaceBezierVisualizer, blendState: BlendState.AlphaBlend);

            uBezier = Materials.NewTypedUniform<Squared.Illuminant.Uniforms.ClampedBezier4>("Bezier");
            
            UIRenderTarget = new AutoRenderTarget(
                RenderCoordinator, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight
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
            if (Window.ClientBounds.Width <= 0)
                return;

            ResizeStartedWhen = Time.Ticks;
            WasResized = true;
        }

        protected override void Update (GameTime gameTime) {
            if (IsResizing || WasResized)
                return;

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
            if (IsResizing)
                return;

            if (WasResized) {
                WasResized = false;

                ReleaseView();

                if (!Graphics.IsFullScreen) {
                    WindowedResolution = new Pair<int>(Window.ClientBounds.Width, Window.ClientBounds.Height);
                    Graphics.PreferredBackBufferWidth = Window.ClientBounds.Width;
                    Graphics.PreferredBackBufferHeight = Window.ClientBounds.Height;
                }
                Graphics.ApplyChangesAfterPresent(RenderCoordinator);
                RenderCoordinator.AfterPresent(() => {
                    Materials.AutoSetViewTransform();
                    UIRenderTarget.Resize(Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight);
                });

                return;
            }

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
                IsActive, PreviousMouseState, MouseState, 
                PreviousKeyboardState, KeyboardState, 
                IsMouseOverUI, UI.KeyboardInputHandler.Buffer
            );

            UI.KeyboardInputHandler.Buffer.Clear();

            using (UI.KeyboardInputHandler.Deactivate())
            using (var group = BatchGroup.ForRenderTarget(frame, -9090, UIRenderTarget)) {
                ClearBatch.AddNew(group, -1, Materials.Clear, clearColor: Color.Transparent);
                Nuklear.Render(gameTime.ElapsedGameTime.Seconds, group, 1);
            }

            Controller.Update();

            if (View != null) {
                MaybeDrawPreviousBitmaps(frame, 3);
                View.Update(this, frame, -10, gameTime.ElapsedGameTime.Ticks);
                ClearBatch.AddNew(frame, -2, Materials.Clear, View.GetData().BackgroundColor, clearZ: 0);
            } else {
                ClearBatch.AddNew(frame, -2, Materials.Clear, Color.Black, clearZ: 0);
            }

            var ir = new ImperativeRenderer(
                frame, Materials, 
                blendState: BlendState.AlphaBlend,
                samplerState: SamplerState.LinearClamp,
                worldSpace: false,
                layer: 9999,
                depthStencilState: SpriteDSS
            );
            ir.UseZBuffer = true;
            ir.UseDiscard = true;

            Materials.ViewportPosition = ViewOffset;
            Materials.ViewportScale = Zoom * Vector2.One;
            Materials.ViewportZRange = new Vector2(-1024, 1024);

            UpdateLightingEnvironment();
            var shouldRenderLighting = (LightingRenderer.Environment?.Lights.Count ?? 0) > 0;
            Texture2D backgroundTexture = null;

            if (View != null) {
                var tloader = (View.Engine != null) ? View.Engine.Configuration.TextureLoader : null;

                var zr = View.GetData()?.ZRange;
                if (zr.HasValue)
                    Materials.ViewportZRange = zr.Value;

                var bg = View.GetData()?.Background;
                if ((bg != null) && (tloader != null)) {
                    bg.EnsureInitialized(tloader);
                    if (bg.IsInitialized)
                        ir.Draw(bg.Instance, Vector2.Zero, origin: Vector2.One * 0.5f, layer: -1, worldSpace: true);
                }

                foreach (var spr in View.GetData().Sprites) {
                    if (tloader == null)
                        continue;
                    if (spr.Texture == null)
                        continue;
                    spr.Texture.EnsureInitialized(tloader);
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
                    if (spr.Z.HasValue)
                        dc.SortKey = spr.Z.Value;
                    ir.Draw(dc, layer: 0, worldSpace: true);
                }
            }

            var elapsedSeconds = TimeSpan.FromTicks(Time.Ticks - LastTimeOverUI).TotalSeconds;
            float uiOpacity = Arithmetic.Lerp(1.0f, 0.4f, (float)((elapsedSeconds - 0.66) * 2.25f));

            ir.Draw(UIRenderTarget, Vector2.Zero, multiplyColor: Color.White * uiOpacity, worldSpace: false);

            LightingRenderer.UpdateFields(frame, -11);

            if (shouldRenderLighting) {
                // NOTE: This needs to happen after the particle system update
                // TODO: Maybe enforce this programmatically?
                var rl = LightingRenderer.RenderLighting(frame, -8, viewportScale: Zoom * Vector2.One);
                rl.Resolve(frame, 4, Vector2.Zero, worldSpace: false, blendState: BlendState.Additive);
            }

            if (View != null) {
                if (!View.GetData().DrawAsBitmaps)
                    View.Draw(this, frame, 3);
            }

            Controller.Draw(this, frame, 5);

            if (ShowPerformanceStats)
                DrawPerformanceStats(ref ir);

            if (IsFirstFrame) {
                IsFirstFrame = false;
                RenderCoordinator.BeforeIssue(() => Materials.PreloadShaders(RenderCoordinator));
            }

            // ThreadPool.QueueUserWorkItem(GCAfterVsync, null);
        }

        private void UpdateLightingEnvironment () {
            var e = LightingRenderer.Environment;

            /*
            e.ZToYMultiplier = 0f;

            if (e.Obstructions.Count == 0) {
                e.Obstructions.Add(new LightObstruction(LightObstructionType.Box, center: new Vector3(106, 48, -8), radius: new Vector3(96, 16, 24)));
                var b = e.Obstructions[0].Bounds3;
                e.Billboards = new[] {
                    new Billboard {
                        Type = BillboardType.Mask,
                        WorldBounds = b,
                        WorldOffset = new Vector3(0, 0, 4)
                    }
                };
            }
            */

            e.Lights.Clear();

            if ((View == null) || (View.Engine == null))
                return;
            var d = View.GetData();

            foreach (var l in d.Lights) {
                var sls = new SphereLightSource {
                    Position = l.WorldPosition,
                    Radius = l.Radius,
                    RampLength = l.Falloff,
                    Color = l.Color.ToVector4()
                };

                if (l.ParticleSystem.TryInitialize(View.Engine.Configuration.SystemResolver)) {
                    var pls = new ParticleLightSource {
                        Template = sls,
                        System = l.ParticleSystem.Instance,
                        StippleFactor = l.ParticleStippleFactor
                    };
                    e.Lights.Add(pls);
                } else {
                    e.Lights.Add(sls);
                }
            }
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
            // FIXME: This relies on the now-removed suspend feature
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
            bool didEnd;
            if (RenderCoordinator.TryWaitForPresentToStart(3, out didEnd, 1) && !didEnd)
                GC.Collect(1, GCCollectionMode.Optimized, false);
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
