using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Microsoft.Xna.Framework.Input;
using Squared.Game;
using Squared.Illuminant;
using Squared.Illuminant.Particles;
using Squared.Illuminant.Particles.Transforms;
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Render.AV1;
using Squared.Render.Convenience;
using Squared.Util;

namespace TestGame.Scenes {
    public class AV1Test : Scene {
        PausableTimeProvider PlaybackTimer;

        Toggle Pause;
        Slider Framerate;
        AV1Video Video;
        long NextFrameWhen = 0;

        public AV1Test (TestGame game, int width, int height)
            : base(game, width, height) {

            Pause.Key = Keys.S;
            Framerate.Min = 5f;
            Framerate.Max = 120f;
            Framerate.Value = 59.94f;
        }

        public override void LoadContent () {
            Video = new AV1Video(Game.RenderCoordinator, "Sparks-5994fps-AV1-10bit-1920x1080-2194kbps.obu", tenBit: true);
            PlaybackTimer = new(Time.DefaultTimeProvider, 0);
            NextFrameWhen = 0;
        }

        public override void UnloadContent () {
            Game.RenderCoordinator.DisposeResource(Video);
        }

        public override void Draw (Frame frame) {
            PlaybackTimer.Paused = Pause;

            var now = PlaybackTimer.Ticks;
            var timeUntilNextFrame = NextFrameWhen - now;
            if (timeUntilNextFrame <= 0) {
                var framerate = Time.TicksFromSeconds(1.0 / Framerate.Value);
                // If we fall behind too far just give up
                if (timeUntilNextFrame > (framerate * 3))
                    NextFrameWhen = now + framerate;
                else
                    NextFrameWhen += framerate;
                Video.AdvanceAsync(true);
            }
            var material = Game.Materials.YUVDecode;

            var ir = new ImperativeRenderer(frame, Game.Materials);
            ir.Clear(layer: 0, color: Color.DeepSkyBlue);

            var mc = Color.White;

            var textures = new TextureSet(Video.YTexture, Video.UTexture);
            ir.Parameters.Add("ThirdTexture", Video.VTexture);
            ir.Draw(textures, Vector2.Zero, layer: 1, scale: Vector2.One, multiplyColor: mc, material: material);
        }

        public override void Update (GameTime gameTime) {
            if (Game.IsActive) {
                var time = (float)Time.Seconds;

                Game.IsMouseVisible = true;
            }
        }
    }
}
