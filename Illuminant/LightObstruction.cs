using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public enum LightObstructionType {
        Ellipsoid,
        Box,
        Cylinder,

        MAX = Cylinder
    }

    public class LightObstruction {
        public readonly LightObstructionType Type;

        public Vector3 Center;
        public Vector3 Size;

        public LightObstruction (
            LightObstructionType type,
            Vector3? center = null,
            Vector3? radius = null
        ) {
            Type = type;
            Center = center.GetValueOrDefault(Vector3.Zero);
            Size = radius.GetValueOrDefault(Vector3.Zero);
        }

        public Bounds3 Bounds3 {
            get {
                return new Bounds3(
                    Center - Size,
                    Center + Size
                );
            }
        }
    }
}
