using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.Illuminant {
    public interface IParticleSystem {
        void Draw (ParticleRenderer renderer, IBatchContainer container, int layer);
    }

    public class ParticleSystem<T> : IParticleSystem
        where T : struct 
    {
        public delegate Vector2 GetPositionDelegate (ref T particle);

        public struct Time {
            public readonly long Ticks;
            public readonly double Seconds;

            private const decimal SecondInTicks = Squared.Util.Time.SecondInTicks;

            public Time (ITimeProvider provider) {
                Ticks = provider.Ticks;
                var decTicks = (decimal)Ticks;
                Seconds = (double)(decTicks / SecondInTicks);
            }
        }

        public abstract class ParticleArgsBase {
            public ParticleCollection Particles;
            public Time PreviousTime, Now;

            internal void SetTime (ITimeProvider timeProvider) {
                PreviousTime = Now;
                Now = new Time(timeProvider);
            }
        }

        public class ParticleRenderArgs : ParticleArgsBase {
            public IBatchContainer Container {
                get;
                internal set;
            }

            public ImperativeRenderer ImperativeRenderer;

            internal void SetContainer (DefaultMaterialSet materials, IBatchContainer container, int layer) {
                Container = container;
                ImperativeRenderer = new ImperativeRenderer(container, materials, layer);
            }
        }

        public class ParticleUpdateArgs : ParticleArgsBase {
        }

        public class ParticleCollection : UnorderedList<T> {
        }

        public readonly Action<ParticleUpdateArgs> Updater;
        public readonly Action<ParticleRenderArgs> Renderer;

        public readonly ITimeProvider TimeProvider;

        public readonly ParticleCollection Particles = new ParticleCollection();

        private readonly ParticleUpdateArgs UpdateArgs = new ParticleUpdateArgs();
        private readonly ParticleRenderArgs RenderArgs = new ParticleRenderArgs();

        public Time LastUpdateTime;
        public Random RNG = new Random();

        public ParticleSystem (
            ITimeProvider timeProvider,
            Action<ParticleUpdateArgs> updater,
            Action<ParticleRenderArgs> renderer
        ) {
            TimeProvider = timeProvider;

            Updater = updater;
            Renderer = renderer;
        }

        public void Update () {
            UpdateArgs.Particles = Particles;
            UpdateArgs.SetTime(TimeProvider);

            Updater(UpdateArgs);

            LastUpdateTime = UpdateArgs.Now;
        }

        public void Draw (ParticleRenderer renderer, IBatchContainer container, int layer) {
            RenderArgs.Particles = Particles;
            RenderArgs.SetTime(TimeProvider);
            RenderArgs.SetContainer(renderer.Materials, container, layer);

            Renderer(RenderArgs);
        }
    }
}
