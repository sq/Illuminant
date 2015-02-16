﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;

namespace Squared.Illuminant {
    public abstract class HeightVolumeBase : IHasBounds {
        public bool IsObstruction = true;

        public readonly Polygon Polygon;
        private float _Height;

        // HACK
        protected VertexPositionColor[] _Mesh3D = null;

        protected HeightVolumeBase (Polygon polygon, float height = 0) {
            Polygon = polygon;
            Height = height;
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
                    new LightPosition(e.Start.X, e.Start.Y, Height),
                    new LightPosition(e.End.X, e.End.Y, Height)
                );
            }
        }
    }

    public class SimpleHeightVolume : HeightVolumeBase {
        public SimpleHeightVolume (Polygon polygon, float height = 0)
            : base (polygon, height) {
        }

        public override VertexPositionColor[] Mesh3D {
            get {
                var c = new Color(Height, Height, Height, 1f);

                if (_Mesh3D == null) {
                    _Mesh3D = (
                        from p in Geometry.Triangulate(Polygon) 
                        from v in p
                        select new VertexPositionColor(
                            new Vector3(v, Height), c                            
                        )
                    ).ToArray();
                } else {
                    for (var i = 0; i < _Mesh3D.Length; i++) {
                        _Mesh3D[i].Position.Z = Height;
                        _Mesh3D[i].Color = c;
                    }
                }

                return _Mesh3D;
            }
        }
    }

    public class WallHeightVolume : HeightVolumeBase {
        float _BottomRightHeight;

        public WallHeightVolume (Polygon polygon, float brHeight = 0, float tlHeight = 1)
            : base (polygon, tlHeight) {

            BottomRightHeight = brHeight;
        }

        public float TopLeftHeight {
            get {
                return Height;
            }
            set {
                Height = value;
            }
        }

        public float BottomRightHeight {
            get {
                return _BottomRightHeight;
            }
            set {
                if ((value < 0) || (value > 1))
                    throw new ArgumentOutOfRangeException("value", "Heights must be [0-1] (lol, d3d9)");

                _BottomRightHeight = value;
            }
        }

        private Color PickVertexColor (Vector2 tl, Vector2 s, Vector2 p) {
            var d = (p.Y - tl.Y) / s.Y;
            var h = MathHelper.Lerp(Height, _BottomRightHeight, d);
            return new Color(h, h, h, 1f);
        }

        public override VertexPositionColor[] Mesh3D {
            get {
                var b = Polygon.Bounds;
                Vector2 tl = b.TopLeft, br = b.BottomRight;
                var s = br - tl;

                if (_Mesh3D == null) {
                    _Mesh3D = (
                        from p in Geometry.Triangulate(Polygon) 
                        from v in p
                        select new VertexPositionColor(
                            new Vector3(v, Height), PickVertexColor(tl, s, v)
                        )
                    ).ToArray();
                } else {
                    for (var i = 0; i < _Mesh3D.Length; i++) {
                        var p = _Mesh3D[i].Position;
                        _Mesh3D[i].Position.Z = Height;
                        _Mesh3D[i].Color = PickVertexColor(tl, s, new Vector2(p.X, p.Y));
                    }
                }

                return _Mesh3D;
            }
        }
    }
}
