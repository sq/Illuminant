using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Illuminant {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ShadowVertex : IVertexType {
        public Vector3 Position;
        public float MinZ;
        public float PairIndex;

        public static VertexDeclaration _VertexDeclaration;

        static ShadowVertex () {
            var tThis = typeof(ShadowVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position") .ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "MinZ")     .ToInt32(), VertexElementFormat.Single,  VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "PairIndex").ToInt32(), VertexElementFormat.Single,  VertexElementUsage.BlendIndices, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PointLightVertex : IVertexType {
        // FIXME: Shouldn't this be V3? Blech
        public Vector2 Position;
        public Vector3 LightCenter;
        public Vector2 Ramp;
        public Vector4 Color;

        public static VertexDeclaration _VertexDeclaration;

        static PointLightVertex () {
            var tThis = typeof(PointLightVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightCenter").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "Ramp").ToInt32(), VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(Marshal.OffsetOf(tThis, "Color").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.Color, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }
}
