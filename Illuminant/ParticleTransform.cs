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

    public class TransformArea {
        public AreaType Type = AreaType.None;
        public Vector3  Center;

        private Vector3 _Size = Vector3.One;
        public Vector3 Size {
            get {
                return _Size;
            }
            set {
                if ((value.X == 0) || (value.Y == 0) || (value.Z == 0))
                    throw new ArgumentOutOfRangeException("value", "Size must be nonzero");
                _Size = value;
            }
        }

        private float _Falloff = 1;
        public float Falloff {
            get {
                return _Falloff;
            }
            set {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException("value", "Falloff divisor must be larger than 0");
                _Falloff = value;
            }
        }

        public TransformArea (AreaType type) {
            Type = type;
        }
    }

    public abstract class ParticleTransform : IDisposable {
        public bool IsActive = true;

        internal abstract Material GetMaterial (ParticleMaterials materials);
        internal abstract void SetParameters (EffectParameterCollection parameters);

        public virtual void Dispose () {
        }
    }

    public abstract class ParticleAreaTransform : ParticleTransform {
        public float Strength = 1;
        public TransformArea Area = null;

        internal override void SetParameters (EffectParameterCollection parameters) {
            if (Area != null) {
                parameters["AreaType"].SetValue((int)Area.Type);
                parameters["AreaCenter"].SetValue(Area.Center);
                parameters["AreaSize"].SetValue(Area.Size);
                parameters["AreaFalloff"].SetValue(Area.Falloff);
            } else {
                parameters["AreaType"].SetValue(0);
            }
            parameters["Strength"].SetValue(Strength);
        }
    }

    public class FMA : ParticleAreaTransform {
        public class FMAParameters<T> where T : struct {
            public T Add;
            public T Multiply;
        }

        public FMAParameters<Vector3> Position;
        public FMAParameters<Vector3> Velocity;
        public FMAParameters<Vector4> Attribute;

        public FMA () {
            Position = new FMAParameters<Vector3> {
                Add = Vector3.Zero,
                Multiply = Vector3.One
            };
            Velocity = new FMAParameters<Vector3> {
                Add = Vector3.Zero,
                Multiply = Vector3.One
            };
            Attribute = new FMAParameters<Vector4> {
                Add = Vector4.Zero,
                Multiply = Vector4.One
            };
        }

        internal override void SetParameters (EffectParameterCollection parameters) {
            base.SetParameters(parameters);
            parameters["PositionAdd"].SetValue(new Vector4(Position.Add, 0));
            parameters["PositionMultiply"].SetValue(new Vector4(Position.Multiply, 1));
            parameters["VelocityAdd"].SetValue(new Vector4(Velocity.Add, 0));
            parameters["VelocityMultiply"].SetValue(new Vector4(Velocity.Multiply, 1));
            parameters["AttributeAdd"].SetValue(Attribute.Add);
            parameters["AttributeMultiply"].SetValue(Attribute.Multiply);
        }

        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.FMA;
        }
    }

    public class MatrixMultiply : ParticleAreaTransform {
        public Matrix Position, Velocity, Attribute;

        public MatrixMultiply () {
            Position = Velocity = Attribute = Matrix.Identity;
        }

        internal override void SetParameters (EffectParameterCollection parameters) {
            base.SetParameters(parameters);
            parameters["PositionMatrix"].SetValue(Position);
            parameters["VelocityMatrix"].SetValue(Velocity);
            parameters["AttributeMatrix"].SetValue(Attribute);
        }

        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.MatrixMultiply;
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
