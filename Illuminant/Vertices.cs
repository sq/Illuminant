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
        public Vector2 A, B, Light;
        public float CornerIndex;

        public static VertexDeclaration _VertexDeclaration;

        static ShadowVertex () {
            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(4 * 2, VertexElementFormat.Vector2, VertexElementUsage.Position, 1),
                new VertexElement(4 * 4, VertexElementFormat.Vector2, VertexElementUsage.Position, 2),
                new VertexElement(4 * 6, VertexElementFormat.Single, VertexElementUsage.BlendIndices, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    public struct PointLightVertex : IVertexType {
        public Vector2 Position;
        public Vector4 Color;
        public float RampStart, RampEnd;

        VertexDeclaration IVertexType.VertexDeclaration {
            get { throw new NotImplementedException(); }
        }
    }
}
