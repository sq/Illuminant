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
    public enum AreaType : int {
        None = 0,
        Ellipsoid = 1,
        Box = 2,
        Cylinder = 3
    }

    internal interface IParticleTransform {
        Material GetMaterial (ParticleMaterials materials);
        void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex);
        Action<DeviceManager, object> BeforeDraw { get; }
        Action<DeviceManager, object> AfterDraw { get; }
    }

    public delegate void ParameterSetter (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex);

    public class TransformArea {
        public AreaType Type = AreaType.None;
        public Parameter<Vector3> Center;
        public Parameter<Vector3> Size = Vector3.One;
        public Parameter<float> Falloff = 1;

        public TransformArea Clone () {
            return (TransformArea)MemberwiseClone();
        }
    }

    public class ParticleTransformUpdateParameters {
        public ParticleSystem System;
        public ParticleSystem.Chunk Chunk, SourceChunk;
        internal ParticleSystem.BufferSet Prev, Curr;
        public Material Material;
        public bool IsUpdate, IsSpawning, ShouldClear;
        public double DeltaTimeSeconds;
        public float Now;
        public int CurrentFrameIndex;
    }
    
    public abstract class ParticleTransform : IDisposable, IParticleTransform {
        internal class UpdateHandler {
            public readonly ParticleTransform Transform;
            public readonly Action<DeviceManager, object> BeforeDraw, AfterDraw;

            public UpdateHandler (ParticleTransform transform) {
                Transform = transform;
                BeforeDraw = _BeforeDraw;
                AfterDraw = _AfterDraw;
            }

            private void _BeforeDraw (DeviceManager dm, object _up) {
                var up = (ParticleTransformUpdateParameters)_up;
                var system = up.System;
                var engine = system.Engine;
                var m = up.Material;
                var e = m.Effect;
                var p = e.Parameters;

                var vp = new Viewport(0, 0, engine.Configuration.ChunkSize, engine.Configuration.ChunkSize);
                if (up.IsUpdate) {
                    up.Curr.Bindings4[2] = new RenderTargetBinding(up.Chunk.RenderColor);
                    up.Curr.Bindings4[3] = new RenderTargetBinding(up.Chunk.RenderData);
                    dm.Device.SetRenderTargets(up.Curr.Bindings4);
                } else if (up.IsSpawning) {
                    up.Curr.Bindings3[2] = up.Chunk.Color;
                    dm.Device.SetRenderTargets(up.Curr.Bindings3);
                } else {
                    dm.Device.SetRenderTargets(up.Curr.Bindings2);
                }
                dm.Device.Viewport = vp;

                if (e != null) {
                    system.SetSystemUniforms(m, up.DeltaTimeSeconds);

                    if (Transform != null)
                        Transform.SetParameters(engine, p, up.Now, up.CurrentFrameIndex);

                    if ((up.Prev != null) || (up.SourceChunk != null)) {
                        var src = up.SourceChunk?.Current ?? up.Prev;
                        p["PositionTexture"].SetValue(src.PositionAndLife);
                        p["VelocityTexture"].SetValue(src.Velocity);

                        var at = p["AttributeTexture"];
                        if (at != null) {
                            if (up.SourceChunk != null)
                                at.SetValue(up.SourceChunk.RenderColor);
                            else
                                at.SetValue(up.IsSpawning ? null : up.Chunk.Color);
                        }

                    }

                    if (up.SourceChunk != null) {
                        p["SourceChunkSizeAndTexel"].SetValue(new Vector3(
                            up.SourceChunk.Size, 1.0f / up.SourceChunk.Size, 1.0f / up.SourceChunk.Size
                        ));
                    }

                    var dft = p["DistanceFieldTexture"];
                    if (dft != null) {
                        dft.SetValue(system.Configuration.Collision?.DistanceField.Texture);

                        var dfu = new Uniforms.DistanceField(
                            system.Configuration.Collision.DistanceField, 
                            system.Configuration.Collision.DistanceFieldMaximumZ.Value
                        );
                        engine.ParticleMaterials.MaterialSet.TrySetBoundUniform(m, "DistanceField", ref dfu);
                    }

                    system.MaybeSetLifeRampParameters(p);
                    system.MaybeSetAnimationRateParameter(p, system.Configuration.Appearance);
                    m.Flush();
                }

                if (up.ShouldClear)
                    dm.Device.Clear(Color.Transparent);
            }

            private void _AfterDraw (DeviceManager dm, object _up) {
                var up = (ParticleTransformUpdateParameters)_up;
                var system = up.System;
                var engine = system.Engine;
                var m = up.Material;
                var e = m.Effect;
                var p = e.Parameters;

                // XNA effectparameter gets confused about whether a value is set or not, so we do this
                //  to ensure it always re-sets the texture parameter
                if (e != null) {
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);

                    var lr = p["LifeRampTexture"];
                    if (lr != null)
                        lr.SetValue((Texture2D)null);

                    var rt = p["LowPrecisionRandomnessTexture"];
                    if (rt != null)
                        rt.SetValue((Texture2D)null);

                    rt = p["RandomnessTexture"];
                    if (rt != null)
                        rt.SetValue((Texture2D)null);

                    var at = p["AttributeTexture"];
                    if (at != null)
                        at.SetValue((Texture2D)null);

                    var dft = p["DistanceFieldTexture"];
                    if (dft != null)
                        dft.SetValue((Texture2D)null);
                }
            }
        }

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
        internal readonly UpdateHandler Handler;

        protected abstract Material GetMaterial (ParticleMaterials materials);
        protected abstract void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex);

        protected ParticleTransform () {
            Handler = new UpdateHandler(this);
        }

        Material IParticleTransform.GetMaterial (ParticleMaterials materials) {
            return GetMaterial(materials);
        }

        void IParticleTransform.SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            SetParameters(engine, parameters, now, frameIndex);
        }

        protected bool BindRandomnessTexture (ParticleEngine e, EffectParameterCollection p, bool highPrecision) {
            var result = false;

            var rt = p["RandomnessTexture"];
            if (rt != null) {
                rt.SetValue(e.RandomnessTexture);
                result = true;
            }

            rt = p["LowPrecisionRandomnessTexture"];
            if (rt != null) {
                rt.SetValue(e.LowPrecisionRandomnessTexture);
                result = true;
            }

            rt = p["RandomnessTexel"];
            if (rt != null) {
                rt.SetValue(new Vector2(
                    1.0f / ParticleEngine.RandomnessTextureWidth, 
                    1.0f / ParticleEngine.RandomnessTextureHeight
                ));
            }

            return result;
        }

        Action<DeviceManager, object> IParticleTransform.BeforeDraw {
            get {
                return Handler.BeforeDraw;
            }
        }

        Action<DeviceManager, object> IParticleTransform.AfterDraw {
            get {
                return Handler.AfterDraw;
            }
        }

        public abstract bool IsValid { get; }

        public virtual void Reset () {
        }

        public virtual void Dispose () {
        }
    }

    public abstract class ParticleAreaTransform : ParticleTransform {
        public float Strength = 1;
        public TransformArea Area = null;

        protected override void SetParameters (ParticleEngine engine, EffectParameterCollection parameters, float now, int frameIndex) {
            if (Area != null) {
                parameters["AreaType"].SetValue((int)Area.Type);
                parameters["AreaCenter"].SetValue(Area.Center.Evaluate(now, engine.ResolveVector3));
                parameters["AreaSize"].SetValue(Area.Size.Evaluate(now, engine.ResolveVector3));
                var falloff = Area.Falloff.Evaluate(now, engine.ResolveSingle);
                falloff = Math.Max(1, falloff);
                parameters["AreaFalloff"].SetValue(falloff);
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

            var ro = parameters["RandomnessOffset"];
            ro.SetValue(new Vector2(
                (float)(CurrentU * 253),
                (float)(CurrentV * 127)
            ));
            ro = parameters["NextRandomnessOffset"];
            ro.SetValue(new Vector2(
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
