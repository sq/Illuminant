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
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.Illuminant.Particles.Transforms {
    public enum AreaType : int {
        None = 0,
        Ellipsoid = 1,
        Box = 2,
        Cylinder = 3,
        Spheroid = 4,
        Octagon = 5
    }

    internal interface IParticleTransform {
        string Label { get; }
        Material GetMaterial (ParticleMaterials materials);
        void SetParameters (ParticleEngine engine, MaterialEffectParameters parameters, float now, int frameIndex);
        Action<DeviceManager, object> BeforeDraw { get; }
        Action<DeviceManager, object> AfterDraw { get; }
        bool IsAnalyzer { get; }
    }

    public delegate void ParameterSetter (ParticleEngine engine, MaterialEffectParameters parameters, float now, int frameIndex);

    public class TransformArea {
        public AreaType Type = AreaType.None;
        public Parameter<Vector3> Center;
        public Parameter<Vector3> Size = Vector3.One;
        public Parameter<float> Falloff = 1;
        public Parameter<float> Rotation = 0;

        public TransformArea Clone () {
            return (TransformArea)MemberwiseClone();
        }
    }

    public class ParticleTransformUpdateParameters {
        public ParticleSystem System;
        public ParticleSystem.Chunk Chunk, SourceChunk;
        internal ParticleSystem.BufferSet Prev, Curr, SourceData;
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
            private bool DidSetRenderTarget;

            public UpdateHandler (ParticleTransform transform) {
                Transform = transform;
                BeforeDraw = _BeforeDraw;
                AfterDraw = _AfterDraw;
            }

            private bool IsValidUpdateTarget (ParticleSystem.Chunk chunk) {
                if (chunk == null)
                    return true;
                return AutoRenderTarget.IsRenderTargetValid(chunk.RenderColor) &&
                    AutoRenderTarget.IsRenderTargetValid(chunk.RenderData);
            }

            private bool IsValidUpdateTarget (ParticleSystem.BufferSet curr) {
                if (curr == null)
                    return true;
                return AutoRenderTarget.IsRenderTargetValid(curr.PositionAndLife) &&
                    AutoRenderTarget.IsRenderTargetValid(curr.Velocity);
            }

            private void _BeforeDraw (DeviceManager dm, object _up) {
                DidSetRenderTarget = false;
                var up = (ParticleTransformUpdateParameters)_up;
                if (up == null)
                    return;
                var system = up.System;
                var engine = system.Engine;
                var m = up.Material;
                var p = m?.Parameters;

                var curr = up.Curr;
                if (!IsValidUpdateTarget(curr))
                    return;
                if (!IsValidUpdateTarget(up.Chunk))
                    return;
                if (!IsValidUpdateTarget(up.Prev))
                    return;
                if (!IsValidUpdateTarget(up.SourceData))
                    return;

                // FIXME: Use a group?
                if (Transform?.IsAnalyzer ?? false) {
                    dm.PushRenderTarget(engine.ScratchTexture);
                } else if (up.IsUpdate) {
                    curr.PushRenderTargets(ref dm, up.Chunk.RenderColor, up.Chunk.RenderData);
                } else if (up.IsSpawning) {
                    curr.PushRenderTargets(ref dm, up.Chunk.Color);
                } else {
                    curr.PushRenderTargets(ref dm);
                }
                DidSetRenderTarget = true;

                if (m != null) {
                    system.SetSystemUniforms(m, up.DeltaTimeSeconds);

                    if (Transform != null)
                        Transform.SetParameters(engine, p, up.Now, up.CurrentFrameIndex);

                    if ((up.Prev != null) || (up.SourceChunk != null)) {
                        if (up.SourceChunk != null) {
                            p["PositionTexture"].SetValue(up.SourceData.PositionAndLife);
                            p["VelocityTexture"].SetValue(up.SourceData.Velocity);
                            var invSize = 1.0f / up.SourceChunk.Size;
                            p["SourceChunkSizeAndTexel"].SetValue(new Vector3(
                                up.SourceChunk.Size, invSize, invSize
                            ));
                        } else {
                            p["PositionTexture"].SetValue(up.Prev.PositionAndLife);
                            p["VelocityTexture"].SetValue(up.Prev.Velocity);
                        }

                        var at = p["AttributeTexture"];
                        if (at != null) {
                            if (up.SourceChunk != null)
                                at.SetValue(up.SourceChunk.RenderColor);
                            else
                                at.SetValue(up.IsSpawning ? null : up.Chunk.Color);
                        }

                    }

                    var dft = p["DistanceFieldTexture"];
                    if (dft != null) {
                        dft.SetValue(system.Configuration.Collision?.DistanceField?.Texture?.Get());

                        var dfu = new Uniforms.DistanceField(
                            system.Configuration.Collision.DistanceField
                        );
                        engine.uDistanceField.Set(m, ref dfu);
                    }

                    m.Effect.Parameters["RotationFromLifeAndIndex"]?.SetValue(new Vector2(
                        MathHelper.ToRadians(system.Configuration.RotationFromLife),
                        MathHelper.ToRadians(system.Configuration.RotationFromIndex)
                    ));

                    system.MaybeSetLifeRampParameters(p);
                    m.Flush(dm);
                }

                if (up.ShouldClear && !(Transform?.IsAnalyzer ?? false))
                    dm.Device.Clear(Color.Transparent);

                Transform?.BeforeUpdateChunk(engine);
            }

            private void _AfterDraw (DeviceManager dm, object _up) {
                var up = (ParticleTransformUpdateParameters)_up;
                var system = up.System;
                var engine = system.Engine;
                var m = up.Material;
                var e = m.Effect;

                // XNA effectparameter gets confused about whether a value is set or not, so we do this
                //  to ensure it always re-sets the texture parameter
                e?.Parameters?.ClearTextures(ParticleSystem.ClearTextureList);

                Transform?.AfterUpdateChunk(engine);

                if (DidSetRenderTarget)
                    dm.PopRenderTarget();
            }
        }

        private bool _IsActive = true, _IsActive2 = true;

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

        public bool IsActive2 {
            get {
                return _IsActive2;
            }
            set {
                if (value == _IsActive2)
                    return;

                _IsActive2 = value;
                ActiveStateChanged?.Invoke();
            }
        }

        public event Action ActiveStateChanged;
        [NonSerialized]
        internal readonly UpdateHandler Handler;

        protected abstract Material GetMaterial (ParticleMaterials materials);
        protected abstract void SetParameters (ParticleEngine engine, MaterialEffectParameters parameters, float now, int frameIndex);

        public string Label { get; set; }
        public bool IsAnalyzer { get; protected set; }

        protected ParticleTransform () {
            Handler = new UpdateHandler(this);
            IsAnalyzer = false;
        }

        Material IParticleTransform.GetMaterial (ParticleMaterials materials) {
            return GetMaterial(materials);
        }

        void IParticleTransform.SetParameters (ParticleEngine engine, MaterialEffectParameters parameters, float now, int frameIndex) {
            SetParameters(engine, parameters, now, frameIndex);
        }

        protected bool BindRandomnessTexture (ParticleEngine e, MaterialEffectParameters p, bool highPrecision) {
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

            p["RandomnessTexel"]?.SetValue(new Vector2(
                1.0f / ParticleEngine.RandomnessTextureWidth, 
                1.0f / ParticleEngine.RandomnessTextureHeight
            ));

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

        public virtual void BeforeFrame (ParticleEngine engine) {
        }

        public virtual void AfterFrame (ParticleEngine engine) {
        }

        protected virtual void BeforeUpdateChunk (ParticleEngine engine) {
        }

        protected virtual void AfterUpdateChunk (ParticleEngine engine) {
        }

        public abstract bool IsValid { get; }

        public virtual void Reset () {
        }

        public virtual void Dispose () {
        }
    }

    public abstract class ParticleAreaTransform : ParticleTransform {
        public float Strength = 1;
        public Vector2? CategoryFilter = null;
        public TransformArea Area = null;

        public static void SetParameters (ParticleEngine engine, MaterialEffectParameters parameters, float now, TransformArea area) {
            if (area != null) {
                parameters["AreaType"].SetValue((int)area.Type);
                parameters["AreaCenter"].SetValue(area.Center.Evaluate(now, engine.ResolveVector3));
                parameters["AreaSize"].SetValue(area.Size.Evaluate(now, engine.ResolveVector3));
                var falloff = area.Falloff.Evaluate(now, engine.ResolveSingle);
                falloff = Math.Max(1, falloff);
                parameters["AreaFalloff"].SetValue(falloff);
                var rotation = area.Rotation.Evaluate(now, engine.ResolveSingle);
                parameters["AreaRotation"].SetValue(rotation);
            } else {
                parameters["AreaType"].SetValue(0);
            }
        }

        protected override void SetParameters (ParticleEngine engine, MaterialEffectParameters parameters, float now, int frameIndex) {
            SetParameters(engine, parameters, now, Area);
            parameters["Strength"]?.SetValue(Strength);
            parameters["CategoryFilter"]?.SetValue(CategoryFilter ?? new Vector2(-9999, 9999));
        }

        public override bool IsValid {
            get {
                return true;
            }
        }
    }
}
