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
        public Vector4 LightProperties;
        public Vector2 MoreLightProperties;
        public Vector4 Color;

        public static VertexDeclaration _VertexDeclaration;

        static SphereLightVertex () {
            var tThis = typeof(SphereLightVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightCenter").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightProperties").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(Marshal.OffsetOf(tThis, "MoreLightProperties").ToInt32(), VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 3),
                new VertexElement(Marshal.OffsetOf(tThis, "Color").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.Color, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DirectionalLightVertex : IVertexType {
        // FIXME: Shouldn't this be V3? Blech
        public Vector2 Position;
        public Vector3 LightDirection;
        public Vector4 LightProperties;
        public Vector3 MoreLightProperties;
        public Vector4 Color;

        public static VertexDeclaration _VertexDeclaration;

        static DirectionalLightVertex () {
            var tThis = typeof(DirectionalLightVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightDirection").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightProperties").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(Marshal.OffsetOf(tThis, "MoreLightProperties").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 3),
                new VertexElement(Marshal.OffsetOf(tThis, "Color").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.Color, 0)
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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BillboardVertex : IVertexType {
        public Vector3 Position;
        public Vector2 TexCoord;
        public Vector3 WorldPosition;
        public Vector3 Normal;
        public float DataScale;

        public static VertexDeclaration _VertexDeclaration;

        static BillboardVertex () {
            var tThis = typeof(BillboardVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "TexCoord").ToInt32(), VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "WorldPosition").ToInt32(),   VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(Marshal.OffsetOf(tThis, "Normal").ToInt32(),   VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "DataScale").ToInt32(),   VertexElementFormat.Single, VertexElementUsage.Normal, 1)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DistanceFunctionVertex : IVertexType {
        public Vector3 Position;
        public Vector3 Center;
        public Vector3 Size;

        public static VertexDeclaration _VertexDeclaration;

        static DistanceFunctionVertex () {
            var tThis = typeof(DistanceFunctionVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "Center").ToInt32(),   VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "Size").ToInt32(),     VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 1)
            );
        }

        public DistanceFunctionVertex (Vector3 position, Vector3 center, Vector3 size) {
            Position = position;
            Center = center;
            Size = size;
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VisualizeDistanceFieldVertex : IVertexType {
        public Vector3 Position;
        public Vector3 RayStart;
        public Vector3 RayVector;
        public Vector4 Color;

        public static VertexDeclaration _VertexDeclaration;

        static VisualizeDistanceFieldVertex () {
            var tThis = typeof(VisualizeDistanceFieldVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(),  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "RayStart").ToInt32(),  VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "RayVector").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(Marshal.OffsetOf(tThis, "Color").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.Color, 0)
            );
        }

        public VisualizeDistanceFieldVertex (Vector3 position, Vector3 rayStart, Vector3 rayVector, Vector4 color) {
            Position = position;
            RayStart = rayStart;
            RayVector = rayVector;
            Color = color;
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }
}
