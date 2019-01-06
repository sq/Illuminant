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
                var curr = up.Curr;
                if (curr.IsDisposed)
                    return;
                if (up.IsUpdate) {
                    curr.Bindings4[2] = new RenderTargetBinding(up.Chunk.RenderColor);
                    curr.Bindings4[3] = new RenderTargetBinding(up.Chunk.RenderData);
                    dm.Device.SetRenderTargets(curr.Bindings4);
                } else if (up.IsSpawning) {
                    curr.Bindings3[2] = up.Chunk.Color;
                    dm.Device.SetRenderTargets(curr.Bindings3);
                } else {
                    dm.Device.SetRenderTargets(curr.Bindings2);
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
                        engine.uDistanceField.Set(m, ref dfu);
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
                    p.ClearTextures(ParticleSystem.ClearTextureList);
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
        [NonSerialized]
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
}
