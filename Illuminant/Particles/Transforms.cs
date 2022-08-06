using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Illuminant.Configuration;
using Squared.Illuminant.Util;
using Squared.Render;
using Squared.Util;

namespace Squared.Illuminant.Particles.Transforms {
    public class FMA : ParticleAreaTransform {
        public class FMAParameters {
            public Parameter<Vector3> Add;
            public Parameter<Vector3> Multiply;
        }

        public float? CyclesPerSecond = 10;
        public FMAParameters Position;
        public FMAParameters Velocity;

        public FMA ()
            : base () {
            Position = new FMAParameters {
                Add = Vector3.Zero,
                Multiply = Vector3.One
            };
            Velocity = new FMAParameters {
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

    public class GeometricTransform : ParticleAreaTransform {
        public class GTParameters {
            public Parameter<float> PreScale = 1;
            public Parameter<Vector3> PreTranslate;
            public Parameter<float> RotationX, RotationY, RotationZ;
            public Parameter<Vector3> PostTranslate;
            public Parameter<float> PostScale = 1;

            public Matrix GetMatrix (ParticleEngine engine, float now) {
                var preScale = PreScale.Evaluate(now, engine.ResolveSingle);
                var postScale = PostScale.Evaluate(now, engine.ResolveSingle);
                var preTranslate = PreTranslate.Evaluate(now, engine.ResolveVector3);
                var postTranslate = PostTranslate.Evaluate(now, engine.ResolveVector3);
                var rotation = new Vector3(
                    MathHelper.ToRadians(RotationX.Evaluate(now, engine.ResolveSingle)),
                    MathHelper.ToRadians(RotationY.Evaluate(now, engine.ResolveSingle)),
                    MathHelper.ToRadians(RotationZ.Evaluate(now, engine.ResolveSingle))
                );

                var result = Matrix.CreateTranslation(preTranslate) *
                    Matrix.CreateScale(preScale);

                if (rotation != Vector3.Zero) {
                    Quaternion quat;
                    Quaternion.CreateFromYawPitchRoll(rotation.Y, rotation.X, rotation.Z, out quat);
                    quat.Normalize();
                    result *= Matrix.CreateFromQuaternion(quat);
                }

                result *= Matrix.CreateScale(postScale);
                result *= Matrix.CreateTranslation(postTranslate);

                return result;
            }
        }

        public float? CyclesPerSecond = 10;
        public GTParameters Position;
        public GTParameters Velocity;

        public GeometricTransform ()
            : base () {
            Position = new GTParameters();
            Velocity = new GTParameters();
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);
            parameters["TimeDivisor"].SetValue(CyclesPerSecond.HasValue ? Uniforms.ParticleSystem.VelocityConstantScale / CyclesPerSecond.Value : -1);
            var position = Position.GetMatrix(engine, now);
            var velocity = Velocity.GetMatrix(engine, now);
            parameters["PositionMatrix"].SetValue(position);
            parameters["VelocityMatrix"].SetValue(velocity);
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.MatrixMultiply;
        }
    }

    public class Noise : ParticleAreaTransform {
        public const float IntervalUnit = 1000;

        public class NoiseParameters<T> where T : struct {
            /// <summary>
            /// This value is subtracted from the noise value before it is scaled.
            /// </summary>
            public Parameter<T> Offset;
            /// <summary>
            /// The noise value before scaling will never be below this amount.
            /// </summary>
            public Parameter<T> Minimum;
            /// <summary>
            /// The noise value is scaled by this amount.
            /// </summary>
            public Parameter<T> Scale;
        }

        public class NoiseParameters4 : NoiseParameters<Vector4> {
        }

        public class NoiseParameters3 : NoiseParameters<Vector3> {
        }

        public class NoiseParametersF : NoiseParameters<float> {
        }

        public float? CyclesPerSecond = 10;
        public NoiseParameters4 Position;
        public NoiseParameters3 Velocity;
        public NoiseParametersF Speed;
        /// <summary>
        /// The number of milliseconds between noise field changes. Changes occur smoothly over time. Set to 0 for no changes.
        /// </summary>
        public Parameter<float> Interval;
        /// <summary>
        /// If set, the velocity of the particles is instantly changed to the new value from the noise field.
        /// </summary>
        public bool ReplaceOldVelocity = true;

        private static int NextSeed = 1;

        [NonSerialized]
        private double LastUChangeWhen;
        [NonSerialized]
        private double CurrentU, CurrentV;
        [NonSerialized]
        private double NextU, NextV;
        [NonSerialized]
        protected CoreCLR.Xoshiro RNG;

        public Noise ()
            : this (null) {
        }

        public Noise (CoreCLR.Xoshiro? rng)
            : base () {
            RNG = rng ?? new CoreCLR.Xoshiro(null);

            Interval = IntervalUnit;
            Position = new NoiseParameters4 {
                Offset = Vector4.One * -0.5f,
                Scale = Vector4.Zero,
            };
            Velocity = new NoiseParameters3 {
                Offset = Vector3.One * -0.5f,
                Scale = Vector3.One,
            };
            Speed = new NoiseParametersF {
                Offset = -0.5f,
                Scale = 0
            };

            Reset();
        }

        private void CycleUVs () {
            CurrentU = NextU;
            CurrentV = NextV;
            NextU = RNG.NextDouble();
            NextV = RNG.NextDouble();
        }

        public override void Reset () {
            base.Reset();

            LastUChangeWhen = 0;
            CycleUVs();
        }

        private void AutoCycleUV (float now, double intervalSecs, out float t) {
            if (intervalSecs <= 0.01) {
                t = 0;
                return;
            }

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
            parameters["PositionMinimum"].SetValue(Position.Minimum.Evaluate(now, engine.ResolveVector4));
            parameters["PositionScale"].SetValue (Position.Scale.Evaluate(now, engine.ResolveVector4));
            parameters["VelocityOffset"].SetValue(new Vector4(Velocity.Offset.Evaluate(now, engine.ResolveVector3), Speed.Offset.Evaluate(now, engine.ResolveSingle)));
            parameters["VelocityMinimum"].SetValue(new Vector4(Velocity.Minimum.Evaluate(now, engine.ResolveVector3), Speed.Minimum.Evaluate(now, engine.ResolveSingle)));
            parameters["VelocityScale"].SetValue (new Vector4(Velocity.Scale.Evaluate(now, engine.ResolveVector3), Speed.Scale.Evaluate(now, engine.ResolveSingle)));

            double intervalSecs = Interval.Evaluate(now, engine.ResolveSingle) / (double)IntervalUnit;
            float t;
            AutoCycleUV(now, intervalSecs, out t);

            var ro = new Vector2((float)(CurrentU * 253), (float)(CurrentV * 127));
            var nro = new Vector2((float)(NextU * 253), (float)(NextV * 127));
            parameters["RandomnessOffset"]?.SetValue(ro);
            parameters["NextRandomnessOffset"]?.SetValue(nro);

            parameters["FrequencyLerp"].SetValue(t);
            parameters["ReplaceOldVelocity"].SetValue(ReplaceOldVelocity? 1f : 0f);
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.Noise;
        }
    }

    public class SpatialNoise : Noise {
        /// <summary>
        /// The scale of the noise field. Larger scale = larger, blurrier pattern.
        /// </summary>
        public Parameter<Vector2> SpaceScale;

        public SpatialNoise ()
            : this (null) {
        }

        public SpatialNoise (CoreCLR.Xoshiro? rng)
            : base (rng) {

            SpaceScale = Vector2.One;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);

            var scale = SpaceScale.Evaluate(now, engine.ResolveVector2);
            parameters["SpaceScale"].SetValue(new Vector2(1.0f / scale.X, 1.0f / scale.Y));
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.SpatialNoise;
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
            /// <summary>
            /// The center of the attractor.
            /// </summary>
            public Parameter<Vector3> Position;
            /// <summary>
            /// The distance from the center of the attractor to its outer edge.
            /// </summary>
            public Parameter<float>   Radius = 1;
            /// <summary>
            /// The strength of the attractor's pull.
            /// </summary>
            public Parameter<float>   Strength = 1;
            /// <summary>
            /// The falloff formula that applies to the attractor's pull.
            /// </summary>
            public AttractorType      Type = AttractorType.Linear;
        }

        /// <summary>
        /// The total forces applied to a particle will not exceed this amount.
        /// </summary>
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

    public class Sensor : ParticleAreaTransform {
        [NonSerialized]
        private object Lock = new object();
        [NonSerialized]
        private UnorderedList<OcclusionQuery> UnusedQueries = new UnorderedList<OcclusionQuery>();
        [NonSerialized]
        private UnorderedList<OcclusionQuery> WaitingQueries = new UnorderedList<OcclusionQuery>();
        [NonSerialized]
        private UnorderedList<OcclusionQuery> UsedQueries = new UnorderedList<OcclusionQuery>();

        [NonSerialized]
        private volatile int _PreviousCount, _Count, _UpdateCount;

        public int PreviousCount {
            get {
                return _PreviousCount;
            }
        }
        public int Count {
            get {
                return _Count;
            }
        }

        private OcclusionQuery ActiveQuery = null;

        public Sensor () {
            IsAnalyzer = true;
        }

        public override void Reset () {
            _PreviousCount = _Count = 0;
        }

        public override void Dispose () {
            lock (Lock) {
                foreach (var q in UsedQueries)
                    q.Dispose();
                foreach (var q in UnusedQueries)
                    q.Dispose();

                UsedQueries.Clear();
                UnusedQueries.Clear();
            }
        }

        protected override Material GetMaterial (ParticleMaterials materials) {
            return materials.CollectParticles;
        }

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            base.SetParameters(engine, parameters, now, frameIndex);
        }

        public override void AfterFrame (ParticleEngine engine) {
            if (IsActive && IsActive2) {
                lock (Lock) {
                    if (_UpdateCount > 0) {
                        // Give up on updating this frame
                        UnusedQueries.AddRange(UsedQueries);
                        UsedQueries.Clear();
                    } else {
                        WaitingQueries.Clear();
                        WaitingQueries.AddRange(UsedQueries);
                        UsedQueries.Clear();
                        Interlocked.Increment(ref _UpdateCount);
                        ThreadPool.QueueUserWorkItem(DoUpdateCount);
                    }
                }
            } else
                _PreviousCount = Interlocked.Exchange(ref _Count, 0);
        }

        private void DoUpdateCount (object _) {
            var started = Time.Ticks;
            var endBy = started + (Time.MillisecondInTicks * 5);

            bool isWaiting = true;

            int count = 0;
            try {
                while (isWaiting && (Time.Ticks <= endBy)) {
                    isWaiting = false;

                    lock (Lock) {
                        using (var e = WaitingQueries.GetEnumerator())
                        while (e.MoveNext()) {
                            var q = e.Current;
                            if (!q.IsComplete) {
                                isWaiting = true;
                                continue;
                            }

                            count += q.PixelCount;
                            e.RemoveCurrent();
                            UnusedQueries.Add(q);
                        }
                    }

                    if (isWaiting)
                        Thread.Sleep(0);
                }

                _PreviousCount = Interlocked.Exchange(ref _Count, count);
            } finally {
                Interlocked.Decrement(ref _UpdateCount);
            }
        }

        protected override void BeforeUpdateChunk (ParticleEngine engine) {
#if FNA
            return;
#endif
            OcclusionQuery query;

            lock (Lock) {
                if (!UnusedQueries.TryPopFront(out query))
                    query = new OcclusionQuery(engine.Coordinator.Device);
                UsedQueries.Add(query);
            }

            var _ = query.IsComplete;
            query.Begin();
            ActiveQuery = query;
        }

        protected override void AfterUpdateChunk (ParticleEngine engine) {
#if FNA
            return;
#endif

            var query = Interlocked.Exchange(ref ActiveQuery, null);
            if (query == null)
                return;

            query.End();
            var _ = query.IsComplete;
        }
    }
}
