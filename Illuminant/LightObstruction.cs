using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public interface ILineWriter {
        void Write (
            Vector2 a, Vector2 aHeights,
            Vector2 b, Vector2 bHeights
        );
    }

    public abstract class LightObstructionBase : IHasBounds {
        public abstract void GenerateLines (ILineWriter output);

        public abstract int LineCount {
            get;
        }

        public abstract Bounds3 Bounds {
            get;
        }

        Bounds IHasBounds.Bounds {
            get { 
                Bounds3 b3 = this.Bounds;
                return b3.XY;
            }
        }
    }

    public class LightObstructionLine : LightObstructionBase {
        public LightPosition A, B;

        public LightObstructionLine (LightPosition a, LightPosition b) {
            A = a;
            B = b;
        }

        public override void GenerateLines (ILineWriter output) {
            var aHeights = new Vector2(0, A.Z);
            var bHeights = new Vector2(0, B.Z);
            output.Write((Vector2)A, aHeights, (Vector2)B, bHeights);
        }

        public override int LineCount {
            get { return 1; }
        }

        public override Bounds3 Bounds {
            get { return new Bounds3(A, B); }
        }
    }

    public class LightObstructionLineStrip : LightObstructionBase {
        public readonly LightPosition[] Points;
        private readonly Bounds3 _Bounds;

        public LightObstructionLineStrip (params LightPosition[] points) {
            if (points.Length < 2)
                throw new ArgumentOutOfRangeException("points", "Need at least two points for a LineStrip");

            Points = points;

            {
                float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;

                foreach (var point in points) {
                    minX = Math.Min(minX, point.X);
                    minY = Math.Min(minY, point.Y);
                    minZ = Math.Min(minZ, point.Z);
                    maxX = Math.Max(maxX, point.X);
                    maxY = Math.Max(maxY, point.Y);
                    maxZ = Math.Max(maxZ, point.Z);
                }

                _Bounds = new Bounds3 {
                    Minimum = new Vector3(minX, minY, minZ),
                    Maximum = new Vector3(maxX, maxY, maxZ)
                };
            }
        }

        public override void GenerateLines (ILineWriter output) {
            for (var i = 1; i < Points.Length; i++) {
                var a = Points[i - 1];
                var aHeights = new Vector2(0, a.Z);
                var b = Points[i];
                var bHeights = new Vector2(0, b.Z);

                output.Write(
                    (Vector2)a, aHeights,
                    (Vector2)b, bHeights
                );
            }
        }

        public override int LineCount {
            get { return Points.Length - 1; }
        }

        public override Bounds3 Bounds {
            get { return _Bounds; }
        }
    }
}
