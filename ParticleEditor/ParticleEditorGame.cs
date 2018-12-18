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
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Text;
using Squared.Util;
using ThreefoldTrials.Framework;
using Nuke = NuklearDotNet.Nuklear;

namespace ParticleEditor {
    public partial class ParticleEditor : MultithreadedGame, INuklearHost {
        public GraphicsDeviceManager Graphics;
        public DefaultMaterialSet Materials { get; private set; }
        public NuklearService Nuklear;
        public IlluminantMaterials IlluminantMaterials;
        public ParticleMaterials ParticleMaterials;

        public KeyboardState PreviousKeyboardState, KeyboardState;
        public MouseState PreviousMouseState, MouseState;

        public Material TextMaterial { get; private set; }

        public FreeTypeFont Font;
        public RenderTarget2D UIRenderTarget;

        private int LastPerformanceStatPrimCount = 0;

        public bool ShowPerformanceStats = false;
        public bool IsMouseOverUI = false;
        public long LastTimeOverUI;

        private bool DidLoadContent;

        public DisplayMode DesktopDisplayMode;
        public Pair<int> WindowedResolution;

        public Model Model;
        public View View;
        public Controller Controller;

        private GCHandle ControllerPin;
        public float Zoom = 1.0f;

        public ParticleEditor () {
            // UniformBinding.ForceCompatibilityMode = true;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredBackBufferFormat = SurfaceFormat.Color;
            Graphics.PreferredDepthStencilFormat = DepthFormat.None;
            Graphics.PreferredBackBufferWidth = 1920;
            Graphics.PreferredBackBufferHeight = 1080;
            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = true;
            Graphics.IsFullScreen = false;

            Content.RootDirectory = "Content";

            UseThreadedDraw = true;
            IsFixedTimeStep = false;

            PreviousKeyboardState = Keyboard.GetState();
            IsMouseVisible = true;
            WindowedResolution = new Pair<int>(1920, 1080);
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

            KeyboardInputHandler = new KeyboardInput(this);
            System.Windows.Forms.Application.AddMessageFilter(KeyboardInputHandler);
        }

        protected override void UnloadContent () {
            base.UnloadContent();

            FreeContent();
        }

        protected override void LoadContent () {
            DidLoadContent = true;

            base.LoadContent();

            Font = new FreeTypeFont(RenderCoordinator, "FiraSans-Medium.otf") {
                SizePoints = 15f,
                GlyphMargin = 2
            };
            Materials = new DefaultMaterialSet(Services);
            IlluminantMaterials = new IlluminantMaterials(Materials);
            ParticleMaterials = new ParticleMaterials(Materials);

            TextMaterial = Materials.Get(Materials.ScreenSpaceShadowedBitmap, blendState: BlendState.AlphaBlend);
            TextMaterial.Parameters.ShadowColor.SetValue(new Vector4(0, 0, 0, 0.5f));
            TextMaterial.Parameters.ShadowOffset.SetValue(Vector2.One);
            
            UIRenderTarget = new RenderTarget2D(
                GraphicsDevice, Graphics.PreferredBackBufferWidth, Graphics.PreferredBackBufferHeight, 
                false, SurfaceFormat.Color, DepthFormat.None, 1, RenderTargetUsage.PlatformContents
            );

            if (Nuklear == null)
                Nuklear = new NuklearService(this) {
                    Font = Font,
                    Scene = UIScene
                };
            else
                Nuklear.Font = Font;

            LastTimeOverUI = Time.Ticks;

            if (Model == null)
                Model = new Model();

            if (View != null)
                View.Dispose();
            View = new View(Model);

            if (Controller == null) {
                Controller = new Controller(Model, View);
                ControllerPin = GCHandle.Alloc(Controller, GCHandleType.Normal);
            } else
                Controller.View = View;

            View.Initialize(this);
        }

        private void Window_ClientSizeChanged (object sender, EventArgs e) {
            Console.WriteLine("ClientSizeChanged");
            if (Window.ClientBounds.Width <= 0)
                return;

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
            PreviousKeyboardState = KeyboardState;
            PreviousMouseState = MouseState;
            KeyboardState = Keyboard.GetState();
            MouseState = Mouse.GetState();

            if (IsActive) {
                var alt = KeyboardState.IsKeyDown(Keys.LeftAlt) || KeyboardState.IsKeyDown(Keys.RightAlt);
                var wasAlt = PreviousKeyboardState.IsKeyDown(Keys.LeftAlt) || PreviousKeyboardState.IsKeyDown(Keys.RightAlt);
                
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

        public override void Draw (GameTime gameTime, Frame frame) {
            if ((Window.ClientBounds.Width != Graphics.PreferredBackBufferWidth) || (Window.ClientBounds.Height != Graphics.PreferredBackBufferHeight))
                return;

            Nuklear.UpdateInput(
                PreviousMouseState, MouseState, 
                PreviousKeyboardState, KeyboardState, 
                IsMouseOverUI, KeyboardInputHandler.Buffer
            );

            KeyboardInputHandler.Buffer.Clear();

            using (var group = BatchGroup.ForRenderTarget(frame, -9990, UIRenderTarget)) {
                ClearBatch.AddNew(group, -1, Materials.Clear, clearColor: Color.Transparent);
                Nuklear.Render(gameTime.ElapsedGameTime.Seconds, group, 1);
            }

            Controller.Update();

            if (IsActive)
                View.Update(this, frame, -2, gameTime.ElapsedGameTime.Ticks);

            ClearBatch.AddNew(frame, -1, Materials.Clear, new Color(0.02f, 0.05f, 0.07f, 1f));

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

            Materials.ViewportPosition = new Vector2(
                -(Graphics.PreferredBackBufferWidth - SidePanelWidth) / 2f,
                -Graphics.PreferredBackBufferHeight / 2f
            ) / Zoom;
            Materials.ViewportScale = Zoom * Vector2.One;
            View.Draw(this, frame, 3);

            if (ShowPerformanceStats)
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
}
