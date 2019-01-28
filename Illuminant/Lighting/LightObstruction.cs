using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public enum LightObstructionType {
        Ellipsoid = 0,
        Box = 1,
        Cylinder = 2,

        MAX = Cylinder
    }

    public class LightObstruction {
        public readonly LightObstructionType Type;

        // If false, this obstruction will be rendered into the static distance field (if any) instead of the dynamic distance field
        public bool IsDynamic = true;

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

        public static LightObstruction Box (Vector3 center, Vector3 size) {
            return new LightObstruction(LightObstructionType.Box, center, size);
        }

        public static LightObstruction Ellipsoid (Vector3 center, Vector3 size) {
            return new LightObstruction(LightObstructionType.Ellipsoid, center, size);
        }

        public override string ToString () {
            return string.Format("{3}{0}@{1} size={2}", Type, Center, Size, IsDynamic ? "dynamic " : "");
        }
    }
}
