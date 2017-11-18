using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant.Transforms {
    public enum AreaType : int {
        None = 0,
        Ellipsoid = 1,
        Box = 2,
        Cylinder = 3
    }

    public abstract class ParticleTransform : IDisposable {
        public bool IsActive = true;

        internal abstract Material GetMaterial (ParticleMaterials materials);
        internal abstract void SetParameters (EffectParameterCollection parameters);

        public virtual void Dispose () {
        }
    }

    public abstract class ParticleAreaTransform : ParticleTransform {
        public AreaType AreaType = AreaType.None;
        public Vector3  AreaCenter;

        private Vector3 _AreaSize = Vector3.One;
        public Vector3 AreaSize {
            get {
                return _AreaSize;
            }
            set {
                if ((value.X == 0) || (value.Y == 0) || (value.Z == 0))
                    throw new ArgumentOutOfRangeException("value", "Size must be nonzero");
                _AreaSize = value;
            }
        }

        private float _AreaFalloff = 1;
        public float AreaFalloff {
            get {
                return _AreaFalloff;
            }
            set {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", "Falloff divisor must be larger than 0");
                _AreaFalloff = value;
            }
        }

        internal override void SetParameters (EffectParameterCollection parameters) {
            parameters["AreaType"].SetValue((int)AreaType);
            parameters["AreaCenter"].SetValue(AreaCenter);
            parameters["AreaSize"].SetValue(_AreaSize);
            parameters["AreaFalloff"].SetValue(_AreaFalloff);
        }
    }

    public abstract class ParticleFMA : ParticleAreaTransform {
        public Vector3 Add, Multiply;

        internal override void SetParameters (EffectParameterCollection parameters) {
            base.SetParameters(parameters);
            parameters["Add"].SetValue(Add);
            parameters["Multiply"].SetValue(Multiply);
        }
    }

    public class PositionFMA : ParticleFMA {
        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.PositionFMA;
        }
    }

    public class VelocityFMA : ParticleFMA {
        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.VelocityFMA;
        }
    }

    public class Gravity : ParticleTransform {
        public class Attractor {
            public Vector3 Position;
            public float   Radius = 1;
            public float   Strength = 1;
        }

        public readonly List<Attractor> Attractors = new List<Attractor>();

        private VertexBuffer InstanceData = null;

        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.Gravity;
        }

        internal override void SetParameters (EffectParameterCollection parameters) {
            if (Attractors.Count > 8)
                throw new Exception("Maximum number of attractors per instance is 8");

            parameters["AttractorCount"].SetValue(Attractors.Count);
            var positions = (from p in Attractors select p.Position).ToArray();
            parameters["AttractorPositions"].SetValue(positions);
            var rns = (from p in Attractors select new Vector2(p.Radius, p.Strength)).ToArray();
            parameters["AttractorRadiusesAndStrengths"].SetValue(rns);
        }
    }
}
