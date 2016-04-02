using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Illuminant {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SphereLightVertex : IVertexType {
        // FIXME: Shouldn't this be V3? Blech
        public Vector2 Position;
        public Vector3 LightCenter;
        public Vector3 RampAndExponential;
        public Vector4 Color;

        public static VertexDeclaration _VertexDeclaration;

        static SphereLightVertex () {
            var tThis = typeof(SphereLightVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(),    VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightCenter").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "RampAndExponential").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(Marshal.OffsetOf(tThis, "Color").ToInt32(),       VertexElementFormat.Vector4, VertexElementUsage.Color, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LightBinVertex : IVertexType {
        // FIXME: Shouldn't this be V3? Blech
        public Vector2 Position;
        public float   LightCount;
        public float   BinIndex;

        public static VertexDeclaration _VertexDeclaration;

        static LightBinVertex () {
            var tThis = typeof(LightBinVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(),   VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightCount").ToInt32(), VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "BinIndex").ToInt32(),   VertexElementFormat.Single, VertexElementUsage.TextureCoordinate, 1)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HeightVolumeVertex : IVertexType {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 ZRange;

        public static VertexDeclaration _VertexDeclaration;

        static HeightVolumeVertex () {
            var tThis = typeof(HeightVolumeVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "Normal").ToInt32(),   VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "ZRange").ToInt32(),   VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0)
            );
        }

        public HeightVolumeVertex (Vector3 position, Vector3 normal, Vector2 zRange) {
            Position = position;
            Normal = normal;
            ZRange = zRange;
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }
}
