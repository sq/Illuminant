using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public enum LightObstructionType : short {
        Ellipsoid = 1,
        Box = 2,
        Cylinder = 3,
        Sphere = 4,
        Octagon = 5
    }

    public class LightObstruction {
        internal const LightObstructionType MAX_Type = LightObstructionType.Sphere;
        public LightObstructionType Type;

        // If false, this obstruction will be rendered into the static distance field (if any) instead of the dynamic distance field
        public bool IsDynamic = true;

        public Vector3 Center;
        public Vector3 Size;
        public float   Rotation;

        public LightObstruction (
            LightObstructionType type,
            Vector3? center   = null,
            Vector3? radius   = null,
            float    rotation = 0
        ) {
            Type = type;
            Center = center.GetValueOrDefault(Vector3.Zero);
            Size = radius.GetValueOrDefault(Vector3.Zero);
            Rotation = rotation;
        }

        public Bounds3 Bounds3 {
            get {
                // FIXME: rotation
                return new Bounds3(
                    Center - Size,
                    Center + Size
                );
            }
        }

        public static LightObstruction Box (Vector3 center, Vector3 size, float rotation = 0) {
            return new LightObstruction(LightObstructionType.Box, center, size, rotation);
        }

        public static LightObstruction Ellipsoid (Vector3 center, Vector3 size, float rotation = 0) {
            return new LightObstruction(LightObstructionType.Ellipsoid, center, size, rotation);
        }

        public static LightObstruction Cylinder (Vector3 center, Vector3 size, float rotation = 0) {
            return new LightObstruction(LightObstructionType.Cylinder, center, size, rotation);
        }

        public override string ToString () {
            return string.Format("{4}{0}@{1} size={2} rotation={3}", Type, Center, Size, Rotation, IsDynamic ? "dynamic " : "");
        }
    }
}
