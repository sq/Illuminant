using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public abstract class HeightVolumeBase : IHasBounds {
        public bool IsObstruction = true;

        public readonly Polygon Polygon;
        private float _ZBase;
        private float _Height;

        // HACK
        protected VertexPositionColor[] _Mesh3D           = null;
        protected FrontFaceVertex[]     _FrontFaceMesh3D  = null;

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
                if ((value < 0) || (value > 1))
                    throw new ArgumentOutOfRangeException("value", "Heights must be [0-1] (lol, d3d9)");

                _ZBase = value;
            }
        }

        public float Height {
            get {
                return _Height;
            }
            set {
                if ((value < 0) || (value > 1))
                    throw new ArgumentOutOfRangeException("value", "Heights must be [0-1] (lol, d3d9)");

                _Height = value;
            }
        }

        public Bounds Bounds {
            get {
                return Polygon.Bounds;
            }
        }

        public abstract VertexPositionColor[] Mesh3D {
            get;
        }

        public abstract ArraySegment<FrontFaceVertex> FrontFaceMesh3D {
            get;
        }

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

            for (var i = 0; i < Polygon.Count; i++) {
                var e = Polygon.GetEdge(i);

                output.Write(
                    new LightPosition(e.Start.X, e.Start.Y, ZBase + Height),
                    new LightPosition(e.End.X, e.End.Y, ZBase + Height)
                );
            }
        }
    }

    public class SimpleHeightVolume : HeightVolumeBase {
        private static readonly short[] _FrontFaceIndices;

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

        public override VertexPositionColor[] Mesh3D {
            get {
                var h = ZBase + Height;
                var c = new Color(h, h, h, 1f);

                if (_Mesh3D == null) {
                    _Mesh3D = (
                        from p in Geometry.Triangulate(Polygon) 
                        from v in p
                        select new VertexPositionColor(
                            new Vector3(v, h), c                            
                        )
                    ).ToArray();
                } else {
                    for (var i = 0; i < _Mesh3D.Length; i++) {
                        _Mesh3D[i].Position.Z = h;
                        _Mesh3D[i].Color = c;
                    }
                }

                return _Mesh3D;
            }
        }

        public override ArraySegment<FrontFaceVertex> FrontFaceMesh3D {
            get {
                if (_FrontFaceMesh3D == null) {
                    var count = (Polygon.Count * 6);
                    _FrontFaceMesh3D = new FrontFaceVertex[count];
                }

                var h = ZBase + Height;
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
                        aNormal = new Vector3((a - prior).PerpendicularLeft(), 0);
                        bNormal = new Vector3((b - a).PerpendicularLeft(), 0);
                    }
                    aNormal.Normalize();
                    bNormal.Normalize();

                    var aTop    = new Vector3(a, h);
                    var aBottom = new Vector3(a, ZBase);
                    var bTop    = new Vector3(b, h);
                    var bBottom = new Vector3(b, ZBase);

                    _FrontFaceMesh3D[i + 0] = new FrontFaceVertex(aTop,    aNormal);
                    _FrontFaceMesh3D[i + 1] = new FrontFaceVertex(bTop,    bNormal);
                    _FrontFaceMesh3D[i + 2] = new FrontFaceVertex(aBottom, aNormal);
                    _FrontFaceMesh3D[i + 3] = new FrontFaceVertex(bTop,    bNormal);
                    _FrontFaceMesh3D[i + 4] = new FrontFaceVertex(bBottom, bNormal);
                    _FrontFaceMesh3D[i + 5] = new FrontFaceVertex(aBottom, aNormal);

                    i += 6;
                    actualCount += 6;
                }

                return new ArraySegment<FrontFaceVertex>(_FrontFaceMesh3D, 0, actualCount);
            }
        }
    }
}
