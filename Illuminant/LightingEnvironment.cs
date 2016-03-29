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

    public class CroppedListLineWriter : ILineWriter {
        public struct Line {
            public readonly Vector2 A, B;
            public readonly Vector2 AHeights, BHeights;

            public Line (Vector2 a, Vector2 aHeights, Vector2 b, Vector2 bHeights) {
                A = a; 
                B = b;
                AHeights = aHeights;
                BHeights = bHeights;
            }
        }

        public Bounds? CropBounds;
        public readonly UnorderedList<Line> Lines = new UnorderedList<Line>();

        public void Write (Vector2 a, Vector2 aHeights, Vector2 b, Vector2 bHeights) {
            if (CropBounds.HasValue) {
                // constructor doesn't get inlined here :(
                Bounds lineBounds;
                lineBounds.TopLeft.X = Math.Min(a.X, b.X);
                lineBounds.TopLeft.Y = Math.Min(a.Y, b.Y);
                lineBounds.BottomRight.X = Math.Max(a.X, b.X);
                lineBounds.BottomRight.Y = Math.Max(a.Y, b.Y);

                if (!CropBounds.Value.Intersects(lineBounds))
                    return;
            }

            Lines.Add(new Line(a, aHeights, b, bHeights));
        }

        public void Reset () {
            Lines.Clear();
        }
    }
}
