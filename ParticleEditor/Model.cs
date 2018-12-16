using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Illuminant.Particles;

namespace ParticleEditor {
    public class Model {
        public string Filename { get; private set; }
        public readonly List<ParticleSystemModel> Systems = new List<ParticleSystemModel>();

        public Model () {
            Filename = "untitled";
        }
    }

    public class ParticleSystemModel {
        public string Name;
        public ParticleSystemConfiguration Configuration;
        public readonly List<ParticleTransformModel> Transforms = new List<ParticleTransformModel>();
    }

    public class ParticleTransformModel {
        public string Name;
        public Type Type;
    }
}
