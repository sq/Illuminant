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
        public readonly List<LightSource> Lights = new List<LightSource>();
        // SDF objects that define obstructions to be rendered into the distance field
        public readonly List<LightObstruction> Obstructions = new List<LightObstruction>();
        // Polygonal meshes that define 3D volumes that are rendered into the distance field
        // In 2.5d mode the volumes' top and front faces are also rendered directly into the scene
        public readonly List<HeightVolumeBase> HeightVolumes = new List<HeightVolumeBase>();
        // A set of g-buffer billboards to paint into the distance field and g-buffer.
        // This is an enumerable so that you can map it to existing objects in your game world
        //  instead of maintaining a separate list.
        public IEnumerable<Billboard> Billboards = null;

        public readonly GIVolumeCollection GIVolumes = new GIVolumeCollection();

        // The Z value of the ground plane.
        public float GroundZ = 0f;

        // The Z value of the sky plane. Objects above this will not be represented in the distance field.
        public float MaximumZ = 128f;

        // Offsets Y coordinates by (Z * -ZToYMultiplier) if TwoPointFiveD is enabled
        public float ZToYMultiplier = 0f;

        // Ambient light color
        public Color Ambient = Color.Black;

        public bool EnableGroundShadows = true;

        public void Clear () {
            Lights.Clear();
            Obstructions.Clear();
            HeightVolumes.Clear();
            // FIXME: Set billboards to null?
        }
    }
}
