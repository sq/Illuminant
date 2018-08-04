﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Illuminant {
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LightVertex : IVertexType {
        // FIXME: Shouldn't this be V3? Blech
        public Vector3 LightCenter;
        public Vector4 LightProperties;
        public Vector4 MoreLightProperties;
        public Vector4 Color;
        public short Corner;
        public short Unused;

        public static VertexDeclaration _VertexDeclaration;

        static LightVertex () {
            var tThis = typeof(LightVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Corner").ToInt32(), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0 ),
                new VertexElement(Marshal.OffsetOf(tThis, "LightCenter").ToInt32(), VertexElementFormat.Vector3, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LightProperties").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1),
                new VertexElement(Marshal.OffsetOf(tThis, "MoreLightProperties").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 3),
                // VertexElementUsage.Color tends to have lower precision for some reason
                new VertexElement(Marshal.OffsetOf(tThis, "Color").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 4)
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

    public struct ParticleSystemVertex : IVertexType {
        public Vector2 Position;
        public short Corner;
        public short Unused;

        public static VertexDeclaration _VertexDeclaration;

        public ParticleSystemVertex (float x, float y, short corner) {
            Position = new Vector2(x, y);
            Corner = corner;
            Unused = corner;
        }

        static ParticleSystemVertex () {
            var tThis = typeof(ParticleSystemVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(),  VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "Corner").ToInt32(), 
                    VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0 )
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    public struct ParticleOffsetVertex : IVertexType {
        public Vector2 Offset;

        public static VertexDeclaration _VertexDeclaration;

        public ParticleOffsetVertex (float x, float y) {
            Offset = new Vector2(x, y);
        }

        static ParticleOffsetVertex () {
            var tThis = typeof(ParticleOffsetVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Offset").ToInt32(),  VertexElementFormat.Vector2, VertexElementUsage.Position, 1)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    public struct GIProbeVertex : IVertexType {
        public Vector4 Position;
        public Vector4 ProbeOffsetAndBaseIndex;
        public Vector4 ProbeIntervalAndCount;

        public static VertexDeclaration _VertexDeclaration;

        public GIProbeVertex (float x, float y, Vector4 offsetAndBaseIndex, Vector4 intervalAndCount) {
            Position = new Vector4(x, y, 0, 1);
            ProbeOffsetAndBaseIndex = offsetAndBaseIndex;
            ProbeIntervalAndCount = intervalAndCount;
        }

        static GIProbeVertex () {
            var tThis = typeof(GIProbeVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "ProbeOffsetAndBaseIndex").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "ProbeIntervalAndCount").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }

    public struct VisualizeGIProbeVertex : IVertexType {
        public Vector4 Position;
        public Vector2 LocalPosition;
        public short   ProbeIndex, Reserved;

        public static VertexDeclaration _VertexDeclaration;

        public VisualizeGIProbeVertex (Vector3 worldPosition, float x, float y, short index, float radius) {
            Position = new Vector4(worldPosition.X - (x * radius), worldPosition.Y - (y * radius), 0, 1);
            LocalPosition = new Vector2(x, y);
            ProbeIndex = Reserved = index;
        }

        static VisualizeGIProbeVertex () {
            var tThis = typeof(VisualizeGIProbeVertex);

            _VertexDeclaration = new VertexDeclaration(
                new VertexElement(Marshal.OffsetOf(tThis, "Position").ToInt32(), VertexElementFormat.Vector4, VertexElementUsage.Position, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "LocalPosition").ToInt32(), VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(Marshal.OffsetOf(tThis, "ProbeIndex").ToInt32(), VertexElementFormat.Short2, VertexElementUsage.BlendIndices, 0)
            );
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }
    }
}
