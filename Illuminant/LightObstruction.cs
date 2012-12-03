using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public class LightObstruction : IHasBounds {
        public Vector2 A, B;

        public LightObstruction (Vector2 a, Vector2 b) {
            A = a;
            B = b;
        }

        public Bounds Bounds {
            get { return new Bounds(A, B); }
        }
    }
}
