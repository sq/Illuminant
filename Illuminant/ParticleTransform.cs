using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant.Particles.Transforms {
    public enum AreaType : int {
        None = 0,
        Ellipsoid = 1,
        Box = 2,
        Cylinder = 3
    }

    internal interface IParticleTransform {
        Material GetMaterial (ParticleMaterials materials);
        void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex);
    }

    public delegate void ParameterSetter (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex);

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

    
    public abstract class ParticleTransform : IDisposable, IParticleTransform {
        public bool IsActive = true;

        protected abstract Material GetMaterial (ParticleMaterials materials);
        protected abstract void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex);

        Material IParticleTransform.GetMaterial (ParticleMaterials materials) {
            return GetMaterial(materials);
        }

        void IParticleTransform.SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex) {
            SetParameters(engine, parameters, frameIndex);
        }

        public virtual void Dispose () {
        }
    }

    public abstract class ParticleAreaTransform : ParticleTransform {
        public float Strength = 1;
        public TransformArea Area = null;

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex) {
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

        public float? CyclesPerSecond = 10;
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

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex) {
            base.SetParameters(engine, parameters, frameIndex);
            parameters["TimeDivisor"].SetValue(CyclesPerSecond.HasValue ? 1000f / CyclesPerSecond.Value : -1);
            parameters["PositionAdd"].SetValue(new Vector4(Position.Add, 0));
            parameters["PositionMultiply"].SetValue(new Vector4(Position.Multiply, 1));
            parameters["VelocityAdd"].SetValue(new Vector4(Velocity.Add, 0));
            parameters["VelocityMultiply"].SetValue(new Vector4(Velocity.Multiply, 1));
            parameters["AttributeAdd"].SetValue(Attribute.Add);
            parameters["AttributeMultiply"].SetValue(Attribute.Multiply);
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.FMA;
        }
    }

    public class MatrixMultiply : ParticleAreaTransform {
        public float? CyclesPerSecond = 10;
        public Matrix Position, Velocity, Attribute;

        public MatrixMultiply () {
            Position = Velocity = Attribute = Matrix.Identity;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex) {
            base.SetParameters(engine, parameters, frameIndex);
            parameters["TimeDivisor"].SetValue(CyclesPerSecond.HasValue ? 1000f / CyclesPerSecond.Value : -1);
            parameters["PositionMatrix"].SetValue(Position);
            parameters["VelocityMatrix"].SetValue(Velocity);
            parameters["AttributeMatrix"].SetValue(Attribute);
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.MatrixMultiply;
        }
    }

    public class Gravity : ParticleTransform {
        public class Attractor {
            public Vector3 Position;
            public float   Radius = 1;
            public float   Strength = 1;
            public bool    Slingshot = true;
        }

        public float MaximumAcceleration = 8;

        public readonly List<Attractor> Attractors = new List<Attractor>();
        [NonSerialized]
        private Vector3[] _Positions;
        [NonSerialized]
        private Vector3[] _RadiusesAndStrengths;

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.Gravity;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, int frameIndex) {
            if (Attractors.Count > 8)
                throw new Exception("Maximum number of attractors per instance is 8");

            if ((_Positions == null) || (_Positions.Length != Attractors.Count))
                _Positions = new Vector3[Attractors.Count];
            if ((_RadiusesAndStrengths == null) || (_RadiusesAndStrengths.Length != Attractors.Count))
                _RadiusesAndStrengths = new Vector3[Attractors.Count];

            for (int i = 0; i < Attractors.Count; i++) {
                _Positions[i] = Attractors[i].Position;
                _RadiusesAndStrengths[i] = new Vector3(Attractors[i].Radius, Attractors[i].Strength, Attractors[i].Slingshot ? 1 : 0);
            }

            parameters["AttractorCount"].SetValue(Attractors.Count);
            parameters["AttractorPositions"].SetValue(_Positions);
            parameters["AttractorRadiusesAndStrengths"].SetValue(_RadiusesAndStrengths);
            parameters["MaximumAcceleration"].SetValue(MaximumAcceleration);
        }
    }
}
