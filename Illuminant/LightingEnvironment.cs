using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Game;

namespace Squared.Illuminant {
    public class LightingEnvironment {
        public readonly List<LightSource> LightSources = new List<LightSource>();
        public readonly SpatialCollection<LightObstruction> Obstructions = new SpatialCollection<LightObstruction>(128f);
    }
}
