using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Illuminant {
    public class LightingEnvironment {
        public readonly List<LightSource> LightSources = new List<LightSource>();
        public readonly List<LightObstruction> Obstructions = new List<LightObstruction>();
    }
}
