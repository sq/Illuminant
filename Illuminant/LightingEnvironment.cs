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

    public class ListLineWriter : ILineWriter {
        public struct Line {
            public readonly Vector2 A, B;

            public Line (Vector2 a, Vector2 b) {
                A = a; 
                B = b;
            }
        }

        public readonly UnorderedList<Line> Lines = new UnorderedList<Line>();

        public void Write (Vector2 a, Vector2 b) {
            Lines.Add(new Line(a, b));
        }

        public void Reset () {
            Lines.Clear();
        }
    }
}
