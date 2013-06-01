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
            public T Particle;
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
            internal bool _Destroy;

            public void Destroy () {
                _Destroy = true;
            }
        }

        public class ParticleCollection : UnorderedList<T> {
        }

        public readonly GetPositionDelegate GetPosition;
        public readonly Func<ParticleUpdateArgs, T> Updater;
        public readonly Action<ParticleRenderArgs> Renderer;

        public readonly ITimeProvider TimeProvider;

        public readonly ParticleCollection Particles = new ParticleCollection();

        private readonly ParticleUpdateArgs UpdateArgs = new ParticleUpdateArgs();
        private readonly ParticleRenderArgs RenderArgs = new ParticleRenderArgs();

        public Time LastUpdateTime;
        public Random RNG = new Random();

        public ParticleSystem (
            ITimeProvider timeProvider,
            Func<ParticleUpdateArgs, T> updater,
            Action<ParticleRenderArgs> renderer,
            GetPositionDelegate getPosition
        ) {
            TimeProvider = timeProvider;

            Updater = updater;
            Renderer = renderer;
            GetPosition = getPosition;
        }

        public void Update () {
            UpdateArgs.SetTime(TimeProvider);

            using (var e = Particles.GetEnumerator())
            while (e.GetNext(out UpdateArgs.Particle)) {
                var newParticle = Updater(UpdateArgs);

                if (UpdateArgs._Destroy) {
                    UpdateArgs._Destroy = false;
                    e.RemoveCurrent();
                } else {
                    e.SetCurrent(ref newParticle);
                }
            }

            LastUpdateTime = UpdateArgs.Now;
        }

        public void Draw (ParticleRenderer renderer, IBatchContainer container, int layer) {
            RenderArgs.SetTime(TimeProvider);
            RenderArgs.SetContainer(renderer.Materials, container, layer);

            using (var e = Particles.GetEnumerator())
            while (e.GetNext(out RenderArgs.Particle))
                Renderer(RenderArgs);
        }
    }
}
