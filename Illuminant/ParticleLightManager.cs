using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class ParticleLightManager<T>
        where T : struct, IParticle<T>
    {
        public delegate void UpdaterDelegate (ParticleSystem<T>.ParticleCollection particles, LightSource lightSource);

        public class Sector : ISpatialPartitionSector, IDisposable {
            public readonly ParticleLightManager<T> Parent;
            public readonly Pair<int> Index;
            public readonly LightSource LightSource;

            public Sector (ParticleLightManager<T> parent, Pair<int> index) {
                Parent = parent;
                Index = index;
                LightSource = new LightSource();

                foreach (var environment in Parent.LightingEnvironments)
                    environment.LightSources.Add(LightSource);
            }

            public void Dispose () {
                foreach (var environment in Parent.LightingEnvironments)
                    environment.LightSources.Remove(LightSource);
            }
        }

        private readonly SpatialPartition<Sector> _Partition;
        private readonly HashSet<Pair<int>> _DeadSectors = new HashSet<Pair<int>>();

        public readonly ParticleSystem<T> System;
        public readonly IEnumerable<LightingEnvironment> LightingEnvironments;
        public readonly UpdaterDelegate Updater;

        public ParticleLightManager (
            ParticleSystem<T> system, 
            IEnumerable<LightingEnvironment> lightingEnvironments,
            UpdaterDelegate updater
        ) {
            System = system;
            _Partition = new SpatialPartition<Sector>(system.Subdivision, (sectorIndex) => new Sector(this, sectorIndex));
            LightingEnvironments = lightingEnvironments;
            Updater = updater;
        }

        public void Update () {
            _DeadSectors.Clear();
            foreach (var sector in _Partition.Sectors)
                _DeadSectors.Add(sector.Index);

            foreach (var sector in System.Particles.Sectors) {
                _DeadSectors.Remove(sector.Index);

                var mySector = _Partition.GetSectorFromIndex(sector.Index, true);
                UpdateLight(sector, mySector);
            }

            foreach (var deadSector in _DeadSectors) {
                _Partition[deadSector].Dispose();
                _Partition.RemoveAt(deadSector);
            }
        }

        protected void UpdateLight (ParticleSystem<T>.ParticleCollection sector, Sector mySector) {
            Updater(sector, mySector.LightSource);
        }
    }
}
