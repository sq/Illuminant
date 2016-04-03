using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingEnvironment {
        public readonly List<LightSource> LightSources = new List<LightSource>();
        // SDF objects that define obstructions to be rendered into the distance field
        public readonly List<LightObstruction> Obstructions = new List<LightObstruction>();
        // Polygonal meshes that define 3D volumes that are rendered into the distance field
        // In 2.5d mode the volumes' top and front faces are also rendered directly into the scene
        public readonly List<HeightVolumeBase> HeightVolumes = new List<HeightVolumeBase>();
        public readonly List<Billboard> Billboards = new List<Billboard>();

        // The Z value of the ground plane.
        public float GroundZ = 0f;

        // The Z value of the sky plane. Objects above this will not be represented in the distance field.
        public float MaximumZ = 1f;

        // Offsets Y coordinates by (Z * -ZToYMultiplier) if TwoPointFiveD is enabled
        public float ZToYMultiplier = 0f;

        public void Clear () {
            LightSources.Clear();
        }
    }
}
