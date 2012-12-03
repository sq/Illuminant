using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace Squared.Illuminant {
    public class LightObstruction {
        public Vector2 A, B;

        public LightObstruction (Vector2 a, Vector2 b) {
            A = a;
            B = b;
        }
    }
}
