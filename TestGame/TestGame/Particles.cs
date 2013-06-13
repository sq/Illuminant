using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Illuminant;

namespace TestGame {
    public struct Spark : IParticle<Spark> {
        public static readonly int DurationInFrames = (int)(60 * 2.75);
        static readonly float fDurationInFrames = DurationInFrames;

        public const float HalfPI = (float)(Math.PI / 2);
        public const float Gravity = 0.075f;
        public const float MaxVelocity = 4f;

        public static readonly Color HotColor = new Color(255, 225, 142);
        public static readonly Color ColdColor = new Color(63, 33, 13);

        public static Texture2D Texture;

        public int FramesLeft;
        public Vector2 Position, PreviousPosition;
        public Vector2 Velocity;

        public Spark (ParticleSystem<Spark> system, Vector2 position, float velocityMagnitude = 1.0f) {
            Position = PreviousPosition = position;
            FramesLeft = system.RNG.Next(DurationInFrames - 4, DurationInFrames + 4);
            Velocity = new Vector2(system.RNG.NextFloat(-2f, 2f) * velocityMagnitude, system.RNG.NextFloat(-3.5f, 1.5f) * velocityMagnitude);
        }

        public static Vector2 ApplyGravity (Vector2 velocity) {
            velocity.Y += Gravity;
            var length = velocity.Length();
            velocity /= length;
            return velocity * Math.Min(length, MaxVelocity);
        }

        public Color GetColor () {
            var lifeLeft = MathHelper.Clamp(FramesLeft / fDurationInFrames, 0, 1);
            var lerpFactor = MathHelper.Clamp((1 - lifeLeft) * 1.4f, 0, 1);
            return Color.Lerp(HotColor, ColdColor, lerpFactor) * ((lifeLeft * 0.85f) + 0.15f);
        }

        public static void Render (ParticleSystem<Spark>.ParticleRenderArgs args) {
            Spark particle;

            while (args.Enumerator.GetNext(out particle)) {
                var delta = particle.Position - particle.PreviousPosition;
                var length = delta.Length();
                var angle = (float)(Math.Atan2(delta.Y, delta.X) - HalfPI);

                args.ImperativeRenderer.Draw(
                    Texture, particle.PreviousPosition,
                    rotation: angle,
                    scale: new Vector2(0.25f, MathHelper.Clamp(length / 5f, 0.05f, 1.75f)),
                    multiplyColor: particle.GetColor(),
                    blendState: BlendState.Additive
                );
            }
        }

        public static Vector2 GetPosition (ref Spark spark) {
            return spark.Position;
        }

        public void InitializeSystem (
            object userData,
            out ParticleSystem<Spark>.UpdateDelegate updater,
            out ParticleSystem<Spark>.RenderDelegate renderer,
            out ParticleSystem<Spark>.GetPositionDelegate getPosition,
            ParticleSystem<Spark> system
        ) {
            var environment = (LightingEnvironment)userData;
            updater = new SparkUpdater(environment).Update;
            renderer = Spark.Render;
            getPosition = Spark.GetPosition;
        }
    }

    public class SparkUpdater {
        public readonly LightingEnvironment LightingEnvironment;
        private readonly CroppedListLineWriter LineWriter;

        public SparkUpdater (LightingEnvironment lightingEnvironment) {
            LightingEnvironment = lightingEnvironment;
            LineWriter = new CroppedListLineWriter();
        }

        public void Update (ParticleSystem<Spark>.ParticleUpdateArgs args) {
            Spark particle;

            LineWriter.Reset();
            LightingEnvironment.EnumerateObstructionLinesInBounds(args.SectorBounds, LineWriter);

            var lines = LineWriter.Lines.GetBuffer();
            var lineCount = LineWriter.Lines.Count;

            while (args.Enumerator.GetNext(out particle)) {
                if (particle.FramesLeft <= 0) {
                    args.Enumerator.RemoveCurrent();
                    continue;
                }

                particle.FramesLeft -= 1;
                particle.PreviousPosition = particle.Position;
                particle.Position += particle.Velocity;

                float distance;
                bool intersected = false;

                for (var i = 0; i < lineCount; i++) {
                    var line = lines[i];

                    if (Geometry.DoLinesIntersect(particle.PreviousPosition, particle.Position, line.A, line.B, out distance)) {
                        var normal = line.B - line.A;
                        normal.Normalize();
                        normal = normal.Perpendicular();

                        // HACK: Fudge factor :(
                        var actualDistanceTravelled = (distance * 0.9f);
                        var intersection = particle.PreviousPosition + (particle.Velocity * actualDistanceTravelled);
                        particle.Position = intersection;

                        var oldVelocity = particle.Velocity;
                        Vector2.Reflect(ref oldVelocity, ref normal, out particle.Velocity);

                        intersected = true;
                        break;
                    }
                }

                if (!intersected)
                    particle.Velocity = Spark.ApplyGravity(particle.Velocity);

                args.ParticleMoved(ref particle, ref particle.PreviousPosition, ref particle.Position);
            }
        }
    }
}
