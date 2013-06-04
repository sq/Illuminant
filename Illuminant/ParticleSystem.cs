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
            public readonly ParticleSystem<T> System;

            public Pair<int> SectorIndex;
            public Bounds SectorBounds;
            public ParticleCollection.Enumerator Enumerator;
            public Time PreviousTime, Now;

            public ParticleArgsBase (ParticleSystem<T> system) {
                System = system;
            }

            internal void SetTime (ITimeProvider timeProvider) {
                PreviousTime = Now;
                Now = new Time(timeProvider);
            }

            internal void SetSector (ParticleCollection sector) {
                SectorIndex = sector.Index;
                SectorBounds = sector.Bounds;
                Enumerator = sector.GetEnumerator();
            }
        }

        public class ParticleRenderArgs : ParticleArgsBase {
            public IBatchContainer Container {
                get;
                internal set;
            }

            public ImperativeRenderer ImperativeRenderer;

            public ParticleRenderArgs (ParticleSystem<T> system)
                : base(system) {
            }

            internal void SetContainer (DefaultMaterialSet materials, IBatchContainer container, int layer) {
                Container = container;
                ImperativeRenderer = new ImperativeRenderer(container, materials, layer);
            }
        }

        public class ParticleUpdateArgs : ParticleArgsBase {
            public ParticleUpdateArgs (ParticleSystem<T> system)
                : base(system) {
            }

            /// <summary>
            /// If you have moved the particle, call this instead of Enumerator.SetCurrent to update it.
            /// This ensures that particle partitioning keeps working.
            /// </summary>
            public void ParticleMoved (ref T particle, ref Vector2 oldPosition, ref Vector2 newPosition) {
                if (!SectorBounds.Contains(ref newPosition)) {
                    var newIndex = System.Particles.GetIndexFromPoint(newPosition);
                    Enumerator.RemoveCurrent();
                    var newSector = System.Particles.GetSectorFromIndex(newIndex, true);
                    newSector.Add(ref particle);
                } else {
                    Enumerator.SetCurrent(ref particle);
                }
            }
        }

        public class ParticleCollection : UnorderedList<T>, ISpatialPartitionSector {
            public readonly Pair<int> Index;
            public readonly Bounds Bounds;

            public ParticleCollection (Pair<int> index, Bounds bounds)
                : base() {

                Index = index;
                Bounds = bounds;
            }
        }

        public readonly Action<ParticleUpdateArgs> Updater;
        public readonly Action<ParticleRenderArgs> Renderer;
        public readonly GetPositionDelegate GetPosition;

        public readonly ITimeProvider TimeProvider;

        public readonly SpatialPartition<ParticleCollection> Particles;

        private readonly ParticleUpdateArgs UpdateArgs;
        private readonly ParticleRenderArgs RenderArgs;

        public Time LastUpdateTime;
        public Random RNG = new Random();

        public ParticleSystem (
            ITimeProvider timeProvider,
            Action<ParticleUpdateArgs> updater,
            Action<ParticleRenderArgs> renderer,
            GetPositionDelegate getPosition
        ) {
            TimeProvider = timeProvider;

            Updater = updater;
            Renderer = renderer;
            GetPosition = getPosition;

            Particles = new SpatialPartition<ParticleCollection>(128.0f, (index) => new ParticleCollection(index, Particles.GetSectorBounds(index)));

            UpdateArgs = new ParticleUpdateArgs(this);
            RenderArgs = new ParticleRenderArgs(this);
        }

        protected void UpdateSector (ParticleCollection sector) {
            if (sector.Count == 0)
                return;

            UpdateArgs.SetSector(sector);
            Updater(UpdateArgs);
            UpdateArgs.Enumerator.Dispose();
        }

        public void Update () {
            UpdateArgs.SetTime(TimeProvider);

            ParticleCollection sector;

            using (var e = Particles.GetSectorsFromBounds(Particles.Extent, false))
            while (e.GetNext(out sector))
                UpdateSector(sector);

            LastUpdateTime = UpdateArgs.Now;
        }

        protected void DrawSector (ParticleCollection sector) {
            if (sector.Count == 0)
                return;

            RenderArgs.SetSector(sector);
            Renderer(RenderArgs);
            RenderArgs.Enumerator.Dispose();
        }

        public void Draw (ParticleRenderer renderer, IBatchContainer container, int layer) {
            RenderArgs.SetTime(TimeProvider);
            RenderArgs.SetContainer(renderer.Materials, container, layer);

            ParticleCollection sector;

            using (var e = Particles.GetSectorsFromBounds(renderer.Viewport, false))
            while (e.GetNext(out sector))
                DrawSector(sector);
        }

        public void Add (T particle) {
            Add(ref particle);
        }

        public void Add (ref T particle) {
            var position = GetPosition(ref particle);
            var sectorIndex = Particles.GetIndexFromPoint(position);
            var sector = Particles.GetSectorFromIndex(sectorIndex, true);

            sector.Add(ref particle);
        }
    }
}
