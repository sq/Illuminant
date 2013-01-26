using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public interface ILineWriter {
        void Write (Vector2 a, Vector2 b);
    }

    public abstract class LightObstructionBase : IHasBounds {
        public abstract void GenerateLines (ILineWriter output);

        public abstract int LineCount {
            get;
        }

        public abstract Bounds Bounds {
            get;
        }
    }

    public class LightObstructionLine : LightObstructionBase {
        public Vector2 A, B;

        public LightObstructionLine (Vector2 a, Vector2 b) {
            A = a;
            B = b;
        }

        public override void GenerateLines (ILineWriter output) {
            output.Write(A, B);
        }

        public override int LineCount {
            get { return 1; }
        }

        public override Bounds Bounds {
            get { return new Bounds(A, B); }
        }
    }

    public class LightObstructionLineStrip : LightObstructionBase {
        public readonly Vector2[] Points;

        public LightObstructionLineStrip (params Vector2[] points) {
            if (points.Length < 2)
                throw new ArgumentOutOfRangeException("points", "Need at least two points for a LineStrip");

            Points = points;
        }

        public override void GenerateLines (ILineWriter output) {
            for (var i = 1; i < Points.Length; i++)
                output.Write(Points[i - 1], Points[i]);
        }

        public override int LineCount {
            get { return Points.Length - 1; }
        }

        public override Bounds Bounds {
            get { return Bounds.FromPoints(Points); }
        }
    }
}
