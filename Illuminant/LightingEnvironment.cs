using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingEnvironment {
        // If you have very detailed obstruction geometry, set this lower to reduce GPU load.
        // For coarse geometry you might want to set this higher to reduce CPU load and memory usage.
        public static float DefaultSubdivision = 128f;

        public readonly List<LightSource> LightSources = new List<LightSource>();
        public readonly SpatialCollection<LightObstructionBase> Obstructions = new SpatialCollection<LightObstructionBase>(DefaultSubdivision);

        public void EnumerateObstructionLinesInBounds (Bounds bounds, ILineWriter output) {
            SpatialCollection<LightObstructionBase>.ItemInfo ii;

            using (var e = Obstructions.GetItemsFromBounds(bounds, false))
            while (e.GetNext(out ii))
                ii.Item.GenerateLines(output);
        }
    }

    public class CroppedListLineWriter : ILineWriter {
        public struct Line {
            public readonly Vector2 A, B;

            public Line (Vector2 a, Vector2 b) {
                A = a; 
                B = b;
            }
        }

        public Bounds? CropBounds;
        public readonly UnorderedList<Line> Lines = new UnorderedList<Line>();

        public void Write (Vector2 a, Vector2 b) {
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

            Lines.Add(new Line(a, b));
        }

        public void Reset () {
            Lines.Clear();
        }
    }
}
