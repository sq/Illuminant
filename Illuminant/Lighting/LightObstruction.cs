using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public enum LightObstructionType : short {
        Ellipsoid = 0,
        Box = 1,
        Cylinder = 2,
        Spheroid = 3,
        Octagon = 4
    }

    public class LightObstruction {
        internal const LightObstructionType MAX_Type = LightObstructionType.Octagon;

        // If false, this obstruction will be rendered into the static distance field (if any) instead of the dynamic distance field
        private bool _IsDynamic;
        public bool IsDynamic {
            get {
                return _IsDynamic;
            }
            set {
                if (_IsDynamic != value)
                    HasDynamicityChanged = true;
                _IsDynamic = value;
            }
        }

        internal bool IsValid = false;
        internal bool HasDynamicityChanged = true;
        internal DistanceFunctionVertex Vertex;

        private LightObstructionType _Type;
        public LightObstructionType Type {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return _Type;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (_Type != value)
                    Invalidate();
                _Type = value;
            }
        }

        public Vector3 Center {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Vertex.Center;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (Vertex.Center != value)
                    Invalidate();
                Vertex.Center = value;
            }
        }

        public Vector3 Size {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Vertex.Size;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (Vertex.Size != value)
                    Invalidate();
                Vertex.Size = value;
            }
        }

        public float Rotation {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Vertex.Rotation;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                if (Vertex.Rotation != value)
                    Invalidate();
                Vertex.Rotation = value;
            }
        }

        public LightObstruction (
            LightObstructionType type,
            Vector3? center   = null,
            Vector3? radius   = null,
            float    rotation = 0
        ) {
            Vertex = default(DistanceFunctionVertex);

            Type = type;
            Center = center.GetValueOrDefault(Vector3.Zero);
            Size = radius.GetValueOrDefault(Vector3.Zero);
            Rotation = rotation;
        }

        internal void Invalidate () {
            IsValid = false;
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
