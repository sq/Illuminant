using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Configuration;
using Squared.Illuminant.Util;
using Squared.Render;

namespace Squared.Illuminant.Particles.Transforms {
    public class FMA : ParticleAreaTransform {
        public class FMAParameters<T> where T : struct {
            public Parameter<T> Add;
            public Parameter<T> Multiply;
        }

        public float? CyclesPerSecond = 10;
        public FMAParameters<Vector3> Position;
        public FMAParameters<Vector3> Velocity;

        public FMA ()
            : base () {
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
            parameters["PositionAdd"].SetValue(new Vector4(Position.Add.Evaluate(now, engine.ResolveVector3), 0));
            parameters["PositionMultiply"].SetValue(new Vector4(Position.Multiply.Evaluate(now, engine.ResolveVector3), 1));
            parameters["VelocityAdd"].SetValue(new Vector4(Velocity.Add.Evaluate(now, engine.ResolveVector3), 0));
            parameters["VelocityMultiply"].SetValue(new Vector4(Velocity.Multiply.Evaluate(now, engine.ResolveVector3), 1));
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.FMA;
        }
    }

    public class MatrixMultiply : ParticleAreaTransform {
        public float? CyclesPerSecond = 10;
        public Matrix Position, Velocity;

        public MatrixMultiply ()
            : base () {
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

    public class Noise : ParticleAreaTransform {
        public const float IntervalUnit = 1000;

        public class NoiseParameters<T> where T : struct {
            public Parameter<T> Offset, Scale;
        }

        public float? CyclesPerSecond = 10;
        public NoiseParameters<Vector4> Position;
        public NoiseParameters<Vector3> Velocity;
        public Parameter<float> Interval;
        public bool ReplaceOldVelocity = true;

        private double LastUChangeWhen;
        private double CurrentU, CurrentV;
        private double NextU, NextV;

        private static int NextSeed = 1;

        [NonSerialized]
        protected readonly MersenneTwister RNG;

        public Noise ()
            : this (null) {
        }

        public Noise (int? seed)
            : base () {
            RNG = new MersenneTwister(seed.GetValueOrDefault(NextSeed++));

            Interval = IntervalUnit;
            Position = new NoiseParameters<Vector4> {
                Offset = Vector4.One * 0.5f,
                Scale = Vector4.Zero,
            };
            Velocity = new NoiseParameters<Vector3> {
                Offset = Vector3.One * 0.5f,
                Scale = Vector3.One,
            };
            LastUChangeWhen = 0;
            CycleUVs();
        }

        private void CycleUVs () {
            CurrentU = NextU;
            CurrentV = NextV;
            NextU = RNG.NextDouble();
            NextV = RNG.NextDouble();
        }

        private void AutoCycleUV (float now, double intervalSecs, out float t) {
            var nextChangeWhen = LastUChangeWhen + intervalSecs;
            if (now >= nextChangeWhen) {
                var elapsed = now - nextChangeWhen;
                if (elapsed >= intervalSecs)
                    LastUChangeWhen = now;
                else
                    LastUChangeWhen = nextChangeWhen;
                nextChangeWhen = LastUChangeWhen + intervalSecs;
                CycleUVs();
            }

            t = (float)((now - LastUChangeWhen) / intervalSecs);
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);

            if (!BindRandomnessTexture(engine, parameters, true))
                return;

            parameters["TimeDivisor"].SetValue(CyclesPerSecond.HasValue ? Uniforms.ParticleSystem.VelocityConstantScale / CyclesPerSecond.Value : -1);
            parameters["PositionOffset"].SetValue(Position.Offset.Evaluate(now, engine.ResolveVector4));
            parameters["PositionScale"].SetValue (Position.Scale.Evaluate(now, engine.ResolveVector4));
            parameters["VelocityOffset"].SetValue(new Vector4(Velocity.Offset.Evaluate(now, engine.ResolveVector3), 0));
            parameters["VelocityScale"].SetValue (new Vector4(Velocity.Scale.Evaluate(now, engine.ResolveVector3), 1));

            double intervalSecs = Interval.Evaluate(now, engine.ResolveSingle) / (double)IntervalUnit;
            float t;
            AutoCycleUV(now, intervalSecs, out t);

            parameters["RandomnessOffset"]?.SetValue(new Vector2(
                (float)(CurrentU * 253),
                (float)(CurrentV * 127)
            ));
            parameters["NextRandomnessOffset"]?.SetValue(new Vector2(
                (float)(NextU * 253),
                (float)(NextV * 127)
            ));

            parameters["FrequencyLerp"].SetValue(t);
            parameters["ReplaceOldVelocity"].SetValue(ReplaceOldVelocity);
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.Noise;
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
                _Positions[i] = Attractors[i].Position.Evaluate(now, engine.ResolveVector3);
                _RadiusesAndStrengths[i] = new Vector3(Attractors[i].Radius.Evaluate(now, engine.ResolveSingle), Attractors[i].Strength.Evaluate(now, engine.ResolveSingle), (int)Attractors[i].Type);
            }

            parameters["AttractorCount"].SetValue(Attractors.Count);
            parameters["AttractorPositions"].SetValue(_Positions);
            parameters["AttractorRadiusesAndStrengths"].SetValue(_RadiusesAndStrengths);
            parameters["MaximumAcceleration"].SetValue(MaximumAcceleration.Evaluate(now, engine.ResolveSingle));
        }

        public override bool IsValid {
            get {
                return Attractors.Count > 0;
            }
        }
    }
}
