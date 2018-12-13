using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Illuminant.Particles;

namespace ParticleEditor {
    public class Model {
        public string Filename { get; private set; }
        public readonly List<ParticleSystemConfiguration> Systems = new List<ParticleSystemConfiguration>();

        public Model () {
            Filename = "untitled";
        }
    }
}
