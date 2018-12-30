using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Configuration;
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
        void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex);
    }

    public delegate void ParameterSetter (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex);

    public class TransformArea {
        public AreaType Type = AreaType.None;
        public Vector3  Center;

        public Vector3 Size = Vector3.One;

        private float _Falloff = 1;
        public float Falloff {
            get {
                return _Falloff;
            }
            set {
                _Falloff = Math.Max(1, value);
            }
        }

        public TransformArea Clone () {
            return (TransformArea)MemberwiseClone();
        }
    }

    
    public abstract class ParticleTransform : IDisposable, IParticleTransform {
        private bool _IsActive;

        public bool IsActive {
            get {
                return _IsActive;
            }
            set {
                if (value == _IsActive)
                    return;

                _IsActive = value;
                ActiveStateChanged?.Invoke();
            }
        }

        public event Action ActiveStateChanged;

        protected abstract Material GetMaterial (ParticleMaterials materials);
        protected abstract void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex);

        Material IParticleTransform.GetMaterial (ParticleMaterials materials) {
            return GetMaterial(materials);
        }

        void IParticleTransform.SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            SetParameters(engine, parameters, now, frameIndex);
        }

        public abstract bool IsValid { get; }

        public virtual void Dispose () {
        }
    }

    public abstract class ParticleAreaTransform : ParticleTransform {
        public float Strength = 1;
        public TransformArea Area = null;

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
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

        public override bool IsValid {
            get {
                return true;
            }
        }
    }

    public class FMA : ParticleAreaTransform {
        public class FMAParameters<T> where T : struct {
            public Parameter<T> Add;
            public Parameter<T> Multiply;
        }

        public float? CyclesPerSecond = 10;
        public FMAParameters<Vector3> Position;
        public FMAParameters<Vector3> Velocity;

        public FMA () {
            Position = new FMAParameters<Vector3> {
                Add = Vector3.Zero,
                Multiply = Vector3.One
            };
            Velocity = new FMAParameters<Vector3> {
                Add = Vector3.Zero,
                Multiply = Vector3.One
            };
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);
            parameters["TimeDivisor"].SetValue(CyclesPerSecond.HasValue ? Uniforms.ParticleSystem.VelocityConstantScale / CyclesPerSecond.Value : -1);
            parameters["PositionAdd"].SetValue(new Vector4(Position.Add.Evaluate(now, engine.Resolve), 0));
            parameters["PositionMultiply"].SetValue(new Vector4(Position.Multiply.Evaluate(now, engine.Resolve), 1));
            parameters["VelocityAdd"].SetValue(new Vector4(Velocity.Add.Evaluate(now, engine.Resolve), 0));
            parameters["VelocityMultiply"].SetValue(new Vector4(Velocity.Multiply.Evaluate(now, engine.Resolve), 1));
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.FMA;
        }
    }

    public class MatrixMultiply : ParticleAreaTransform {
        public float? CyclesPerSecond = 10;
        public Matrix Position, Velocity;

        public MatrixMultiply () {
            Position = Velocity = Matrix.Identity;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);
            parameters["TimeDivisor"].SetValue(CyclesPerSecond.HasValue ? Uniforms.ParticleSystem.VelocityConstantScale / CyclesPerSecond.Value : -1);
            parameters["PositionMatrix"].SetValue(Position);
            parameters["VelocityMatrix"].SetValue(Velocity);
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.MatrixMultiply;
        }
    }

    public enum AttractorType {
        Physical = 0,
        Linear = 1,
        Exponential = 2
    }

    public class Gravity : ParticleTransform {
        public const int MaxAttractors = 16;

        public class Attractor {
            public Parameter<Vector3> Position;
            public Parameter<float>   Radius = 1;
            public Parameter<float>   Strength = 1;
            public AttractorType      Type = AttractorType.Linear;
        }

        public Parameter<float> MaximumAcceleration = 8;

        public readonly List<Attractor> Attractors = new List<Attractor>();
        [NonSerialized]
        private Vector3[] _Positions;
        [NonSerialized]
        private Vector3[] _RadiusesAndStrengths;

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.Gravity;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            if (Attractors.Count > MaxAttractors)
                throw new Exception("Maximum number of attractors per instance is " + MaxAttractors);

            if ((_Positions == null) || (_Positions.Length != Attractors.Count))
                _Positions = new Vector3[Attractors.Count];
            if ((_RadiusesAndStrengths == null) || (_RadiusesAndStrengths.Length != Attractors.Count))
                _RadiusesAndStrengths = new Vector3[Attractors.Count];

            for (int i = 0; i < Attractors.Count; i++) {
                _Positions[i] = Attractors[i].Position.Evaluate(now, engine.Resolve);
                _RadiusesAndStrengths[i] = new Vector3(Attractors[i].Radius.Evaluate(now, engine.Resolve), Attractors[i].Strength.Evaluate(now, engine.Resolve), (int)Attractors[i].Type);
            }

            parameters["AttractorCount"].SetValue(Attractors.Count);
            parameters["AttractorPositions"].SetValue(_Positions);
            parameters["AttractorRadiusesAndStrengths"].SetValue(_RadiusesAndStrengths);
            parameters["MaximumAcceleration"].SetValue(MaximumAcceleration.Evaluate(now, engine.Resolve));
        }

        public override bool IsValid {
            get {
                return Attractors.Count > 0;
            }
        }
    }
}
