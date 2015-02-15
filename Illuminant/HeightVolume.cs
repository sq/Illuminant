using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;

namespace Squared.Illuminant {
    public class HeightVolume : IHasBounds {
        public readonly Polygon Polygon;
        private float _Height;

        // HACK
        private VertexPositionColor[] _Mesh3D = null;
        private static readonly Random rng = new Random();

        public HeightVolume (Polygon polygon, float height = 0) {
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

        public VertexPositionColor[] Mesh3D {
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

        public int LineCount {
            get {
                return Polygon.Count;
            }
        }

        public void GenerateLines (ILineWriter output) {
            for (var i = 0; i < Polygon.Count; i++) {
                var e = Polygon.GetEdge(i);

                output.Write(
                    new LightPosition(e.Start.X, e.Start.Y, Height),
                    new LightPosition(e.End.X, e.End.Y, Height)
                );
            }
        }
    }
}
