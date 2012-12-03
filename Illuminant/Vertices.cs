using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Illuminant {
    [StructLayout(LayoutKind.Sequential, Pack=0)]
    public struct ShadowVertex : IVertexType {
        public Vector2 A, B;
        public float CornerIndex;

        public static VertexDeclaration _VertexDeclaration;

        static ShadowVertex () {
            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(4 * 2, VertexElementFormat.Vector2, VertexElementUsage.Position, 1),
                new VertexElement(4 * 4, VertexElementFormat.Single, VertexElementUsage.BlendIndices, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct PointLightVertex : IVertexType {
        public Vector2 Position, LightCenter, Ramp;
        public Vector4 Color;

        public static VertexDeclaration _VertexDeclaration;

        static PointLightVertex () {
            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(4 * 2, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(4 * 4, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(4 * 6, VertexElementFormat.Vector4, VertexElementUsage.Color, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }
}
