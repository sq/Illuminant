using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Content;
using Squared.Render;

namespace Squared.Illuminant {
    public partial class ParticleEngine : IDisposable {
        public bool IsDisposed { get; private set; }
        
        public readonly RenderCoordinator           Coordinator;

        public readonly DefaultMaterialSet          Materials;
        public          ParticleMaterials           ParticleMaterials { get; private set; }

        public readonly ParticleEngineConfiguration Configuration;

        public ParticleEngine (
            ContentManager content, RenderCoordinator coordinator, 
            DefaultMaterialSet materials, ParticleEngineConfiguration configuration
        ) {
            Coordinator = coordinator;
            Materials = materials;

            ParticleMaterials = new ParticleMaterials(materials);
            Configuration = configuration;

            lock (coordinator.CreateResourceLock) {
            }

            LoadMaterials(content);

            Coordinator.DeviceReset += Coordinator_DeviceReset;
        }

        private void Coordinator_DeviceReset (object sender, EventArgs e) {
            // FillIndexBuffer();
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;            
        }
    }

    public class ParticleEngineConfiguration {
    }
}
