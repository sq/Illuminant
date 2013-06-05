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
        void Update ();
        void Draw (ParticleRenderer renderer, IBatchContainer container, int layer);
        void Clear ();
        int Count { get; }
    }

    public interface IParticle<T>
        where T : struct, IParticle<T> 
    {
        void InitializeSystem (
            object userData,
            out ParticleSystem<T>.UpdateDelegate updater, 
            out ParticleSystem<T>.RenderDelegate renderer, 
            out ParticleSystem<T>.GetPositionDelegate getPosition,
            ParticleSystem<T> system
        ); 
    }

    public class ParticleSystem<T> : IParticleSystem
        where T : struct, IParticle<T>
    {
        public delegate void UpdateDelegate (ParticleUpdateArgs args);
        public delegate void RenderDelegate (ParticleRenderArgs args);
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
                    if (newSector.Count > System.SectorCapacityLimit) {
                        System._ParticlesRemovedByLimit += 1;

                        if (System.RemoveParticlesWhenCapacityReached)
                            newSector.RemoveAt(System.RNG.Next(0, newSector.Count));
                        else
                            return;
                    }
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

        public bool RemoveParticlesWhenCapacityReached;
        public int? CapacityLimit;
        public int? SectorCapacityLimit;

        public readonly UpdateDelegate Updater;
        public readonly RenderDelegate Renderer;
        public readonly GetPositionDelegate GetPosition;

        public readonly ITimeProvider TimeProvider;

        public readonly SpatialPartition<ParticleCollection> Particles;

        private readonly ParticleUpdateArgs UpdateArgs;
        private readonly ParticleRenderArgs RenderArgs;

        private readonly UnorderedList<ParticleCollection> _SectorsFromLastUpdate = new UnorderedList<ParticleCollection>();
        private int _PreviousCount, _Count;
        private int _ParticlesRemovedByLimit;

        public Time LastUpdateTime;
        public Random RNG = new Random();

        public ParticleSystem (ITimeProvider timeProvider, object userData = null) {
            TimeProvider = timeProvider;

            Particles = new SpatialPartition<ParticleCollection>(128.0f, (index) => new ParticleCollection(index, Particles.GetSectorBounds(index)));

            UpdateArgs = new ParticleUpdateArgs(this);
            RenderArgs = new ParticleRenderArgs(this);

            var temporaryInstance = Activator.CreateInstance<T>();
            temporaryInstance.InitializeSystem(userData, out Updater, out Renderer, out GetPosition, this);
        }

        protected void UpdateSector (ParticleCollection sector) {
            if (sector.Count == 0)
                return;

            _SectorsFromLastUpdate.Add(sector);

            UpdateArgs.SetSector(sector);
            Updater(UpdateArgs);
            UpdateArgs.Enumerator.Dispose();
        }

        public void Update () {
            if (CapacityLimit.HasValue && CapacityLimit.Value < 1)
                throw new ArgumentOutOfRangeException("CapacityLimit");
            if (SectorCapacityLimit.HasValue && SectorCapacityLimit.Value < 1)
                throw new ArgumentOutOfRangeException("SectorCapacityLimit");

            _SectorsFromLastUpdate.Clear();

            UpdateArgs.SetTime(TimeProvider);

            ParticleCollection sector;

            var newCount = 0;

            using (var e = Particles.GetSectorsFromBounds(Particles.Extent, false))
            while (e.GetNext(out sector)) {
                UpdateSector(sector);
                newCount += sector.Count;
            }

            _PreviousCount = _Count;
            _Count = newCount;

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

            _ParticlesRemovedByLimit = 0;
        }

        public bool Add (T particle) {
            return Add(ref particle);
        }

        public bool Add (ref T particle) {
            if (CapacityLimit.HasValue && _Count >= CapacityLimit.Value) {
                _ParticlesRemovedByLimit += 1;

                if (!RemoveParticlesWhenCapacityReached || !RemoveRandomParticle(null))
                    return false;
            }

            var position = GetPosition(ref particle);
            var sectorIndex = Particles.GetIndexFromPoint(position);
            var sector = Particles.GetSectorFromIndex(sectorIndex, true);

            if (SectorCapacityLimit.HasValue && sector.Count >= SectorCapacityLimit.Value) {
                _ParticlesRemovedByLimit += 1;

                if (!RemoveParticlesWhenCapacityReached || !RemoveRandomParticle(sector))
                    return false;
            }

            sector.Add(ref particle);
            _Count += 1;
            return true;
        }

        public bool RemoveRandomParticle (ParticleCollection sector) {
            // If no sector is provided to remove from, semirandomly pick a sector from the last update.
            if (sector == null) {
                if (_SectorsFromLastUpdate.Count == 0)
                    return false;

                var index = RNG.Next(0, _SectorsFromLastUpdate.Count);
                sector = _SectorsFromLastUpdate.GetBuffer()[index];
            }

            if (sector.Count == 0)
                return false;

            var particleIndex = RNG.Next(0, sector.Count);
            sector.RemoveAt(particleIndex);

            return true;
        }

        public void Clear () {
            Particles.Clear();
            _SectorsFromLastUpdate.Clear();
            _Count = 0;
        }

        public int ParticlesRemovedByLimit {
            get {
                return _ParticlesRemovedByLimit;
            }
        }

        public int PreviousCount {
            get {
                return _PreviousCount;
            }
        }

        /// <summary>
        /// As of most recent call to Update.
        /// </summary>
        public int Count {
            get {
                return _Count;
            }
        }
    }
}
