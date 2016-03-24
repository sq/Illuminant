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

    public enum LightObstructionType {
        Sphere,
        Cube
    }

    public class LightObstruction {
        public readonly LightObstructionType Type;

        public Vector3 Center;

        // For some types only the first element of this is used
        public Vector3 Size;
    }
}
