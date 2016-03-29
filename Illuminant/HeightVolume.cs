using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
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

    public abstract class HeightVolumeBase : IHasBounds {
        public bool IsObstruction = true;

        public readonly Polygon Polygon;
        private float _ZBase;
        private float _Height;

        // HACK
        protected HeightVolumeVertex[] _Mesh3D = null;
        protected HeightVolumeVertex[] _FrontFaceMesh3D = null;

        protected HeightVolumeBase (Polygon polygon, float zBase = 0, float height = 0) {
            Polygon = polygon;
            ZBase = zBase;
            Height = height;
        }

        public float ZBase {
            get {
                return _ZBase;
            }
            set {
                _FrontFaceMesh3D = null;
                _ZBase = value;
            }
        }

        public float Height {
            get {
                return _Height;
            }
            set {
                _FrontFaceMesh3D = null;
                _Height = value;
            }
        }

        public Bounds Bounds {
            get {
                return Polygon.Bounds;
            }
        }

        public abstract HeightVolumeVertex[] Mesh3D {
            get;
        }

        public abstract ArraySegment<HeightVolumeVertex> GetFrontFaceMesh3D ();

        public int LineCount {
            get {
                return
                    IsObstruction
                        ? Polygon.Count
                        : 0;
            }
        }

        public virtual void GenerateLines (ILineWriter output) {
            if (!IsObstruction) 
                return;

            var heights = new Vector2(ZBase, ZBase + Height);

            for (var i = 0; i < Polygon.Count; i++) {
                var e = Polygon.GetEdge(i);

                output.Write(
                    e.Start, heights,
                    e.End, heights
                );
            }
        }
    }

    public class SimpleHeightVolume : HeightVolumeBase {
        private static readonly short[] _FrontFaceIndices;

        private ArraySegment<HeightVolumeVertex> _FrontFaceMesh3DSegment;

        static SimpleHeightVolume () {
            _FrontFaceIndices = new short[2048 * 6];

            for (int i = 0, j = 0; i < _FrontFaceIndices.Length; i += 6, j += 2) {
                short tl = (short)(j + 0), tr = (short)(j + 2);
                short bl = (short)(j + 1), br = (short)(j + 3);

                _FrontFaceIndices[i + 0] = tl;
                _FrontFaceIndices[i + 1] = tr;
                _FrontFaceIndices[i + 2] = bl;
                _FrontFaceIndices[i + 3] = tr;
                _FrontFaceIndices[i + 4] = br;
                _FrontFaceIndices[i + 5] = bl;
            }
        }

        public SimpleHeightVolume (Polygon polygon, float zBase = 0, float height = 0)
            : base (polygon, zBase, height) {
        }

        public override HeightVolumeVertex[] Mesh3D {
            get {
                var h1 = ZBase;
                var h2 = ZBase + Height;
                var range = new Vector2(h1, h2);

                if (_Mesh3D == null) {
                    _Mesh3D = (
                        from p in Geometry.Triangulate(Polygon) 
                        from v in p
                        select new HeightVolumeVertex(
                            new Vector3(v, h2), Vector3.UnitZ, range
                        )
                    ).ToArray();
                } else {
                    for (var i = 0; i < _Mesh3D.Length; i++) {
                        _Mesh3D[i].Position.Z = h2;
                        _Mesh3D[i].ZRange = range;
                    }
                }

                return _Mesh3D;
            }
        }

        public override ArraySegment<HeightVolumeVertex> GetFrontFaceMesh3D () {
            var h1 = ZBase;
            var h2 = ZBase + Height;
            var zRange = new Vector2(h1, h2);

            if (_FrontFaceMesh3D != null) {
                if (
                    (_FrontFaceMesh3D[0].ZRange != zRange) ||
                    (_FrontFaceMesh3D[1].ZRange != zRange)
                )
                    throw new InvalidDataException();

                // FIXME
                return _FrontFaceMesh3DSegment;
            }

            var count = (Polygon.Count * 6);
            _FrontFaceMesh3D = new HeightVolumeVertex[count];

            var actualCount = 0;

            for (int i = 0, j = 0; j < Polygon.Count; j += 1) {
                var priorEdge = Polygon.GetEdge(j - 1);
                var edge = Polygon.GetEdge(j);
                var prior = priorEdge.Start;
                var a = edge.Start;
                var b = edge.End;

                // GROSS HACK: Cull backfaces.
                // We have no simple way to do this because we don't have winding information...
                var pA = Geometry.LineIntersectPolygon(
                    a + new Vector2(0, 0.1f), 
                    a + new Vector2(0, 999f),
                    Polygon
                );
                var pB = Geometry.LineIntersectPolygon(
                    b + new Vector2(0, 0.1f), 
                    b + new Vector2(0, 999f),
                    Polygon
                );

                if (pA.HasValue || pB.HasValue)
                    continue;

                Vector3 aNormal, bNormal;
                    
                // HACK: To produce sensible normals for horizontal surfaces. Blech.
                if (a.Y == b.Y) {
                    aNormal = bNormal = new Vector3(0, 1, 0);
                } else {
                    if (a == prior)
                        aNormal = Vector3.Zero;
                    else {
                        aNormal = new Vector3((a - prior).PerpendicularLeft(), 0);
                        aNormal.Normalize();
                    }

                    if (b == a)
                        bNormal = Vector3.Zero;
                    else {
                        bNormal = new Vector3((b - a).PerpendicularLeft(), 0);
                        bNormal.Normalize();
                    }
                }

                if (
                    float.IsNaN(aNormal.X) ||
                    float.IsNaN(aNormal.Y) ||
                    float.IsNaN(aNormal.Z) ||
                    float.IsNaN(bNormal.X) ||
                    float.IsNaN(bNormal.Y) ||
                    float.IsNaN(bNormal.Z)
                )
                    Debugger.Break();

                var aTop    = new Vector3(a, h2);
                var aBottom = new Vector3(a, h1);
                var bTop    = new Vector3(b, h2);
                var bBottom = new Vector3(b, h1);

                _FrontFaceMesh3D[i + 0] = new HeightVolumeVertex(aTop,    aNormal, zRange);
                _FrontFaceMesh3D[i + 1] = new HeightVolumeVertex(bTop,    bNormal, zRange);
                _FrontFaceMesh3D[i + 2] = new HeightVolumeVertex(aBottom, aNormal, zRange);
                _FrontFaceMesh3D[i + 3] = new HeightVolumeVertex(bTop,    bNormal, zRange);
                _FrontFaceMesh3D[i + 4] = new HeightVolumeVertex(bBottom, bNormal, zRange);
                _FrontFaceMesh3D[i + 5] = new HeightVolumeVertex(aBottom, aNormal, zRange);

                i += 6;
                actualCount += 6;
            }

            return _FrontFaceMesh3DSegment = new ArraySegment<HeightVolumeVertex>(_FrontFaceMesh3D, 0, actualCount);
        }
    }
}
