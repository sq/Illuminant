using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingQuery {
        private struct DeltaLine {
            public readonly float X1, Y1;
            public readonly float LengthX, LengthY;

            public DeltaLine (Vector2 a, Vector2 b) {
                X1 = a.X;
                Y1 = a.Y;
                LengthX = b.X - a.X;
                LengthY = b.Y - a.Y;
            }
        }

        private struct DeltaLineWriter : ILineWriter {
            public readonly UnorderedList<DeltaLine> Lines;

            public DeltaLineWriter (UnorderedList<DeltaLine> buffer) {
                Lines = buffer;
            }

            public void Write (Vector2 a, Vector2 b) {
                Lines.Add(new DeltaLine(a, b));
            }

            public void Reset () {
                Lines.Clear();
            }
        }

        public readonly LightingEnvironment Environment;

        private readonly Dictionary<LightSource, DeltaLine[]> _ObstructionsByLight = 
                new Dictionary<LightSource, DeltaLine[]>(new ReferenceComparer<LightSource>());

        private static class UnorderedListPool<T> {
            const int Capacity = 4;
            private static readonly ConcurrentBag<UnorderedList<T>> UnusedLists =
                new ConcurrentBag<UnorderedList<T>>();

            public static UnorderedList<T> Allocate (int? initialCapacity = null) {
                UnorderedList<T> result;

                if (!UnusedLists.TryTake(out result)) {
                    if (initialCapacity.HasValue)
                        result = new UnorderedList<T>(initialCapacity.Value);
                    else
                        result = new UnorderedList<T>();
                }

                result.Clear();
                return result;
            }

            public static void Free (UnorderedList<T> queue) {
                queue.Clear();

                if (UnusedLists.Count > Capacity)
                    return;

                UnusedLists.Add(queue);
            }
        }

        private struct LineGeneratorContext : IDisposable {
            public readonly DeltaLineWriter LineWriter;
            public readonly UnorderedList<KeyValuePair<LightSource, DeltaLine[]>> Queue;

            public LineGeneratorContext (int initialCapacity) {
                LineWriter = new DeltaLineWriter(UnorderedListPool<DeltaLine>.Allocate(initialCapacity));
                Queue = UnorderedListPool<KeyValuePair<LightSource, DeltaLine[]>>.Allocate();
            }

            public void Dispose () {
                UnorderedListPool<DeltaLine>.Free(LineWriter.Lines);
                UnorderedListPool<KeyValuePair<LightSource, DeltaLine[]>>.Free(Queue);
            }
        }

        public LightingQuery (LightingEnvironment environment, bool parallelCreate) {
            Environment = environment;

            Update(parallelCreate);
        }

        public void Update (bool parallelUpdate) {
            var options = new ParallelOptions();
            if (!parallelUpdate)
                options.MaxDegreeOfParallelism = 1;

            _ObstructionsByLight.Clear();
#if SDL2
            // Parallel is Satan -flibit
            foreach (LightSource ls in Environment.LightSources)
            {
                LineGeneratorContext ctx = new LineGeneratorContext(Environment.Obstructions.Count * 2);
                GenerateLinesForLightSource(ref ctx, ls);
                lock (_ObstructionsByLight)
                {
                    foreach (var kvp in ctx.Queue)
                    {
                        _ObstructionsByLight.Add(kvp.Key, kvp.Value);
                    }
                }
                ctx.Dispose();
            }
#else
            using (var buffer = BufferPool<LightSource>.Allocate(Environment.LightSources.Count)) {
                Environment.LightSources.CopyTo(buffer.Data);

                Parallel.For(
                    0, Environment.LightSources.Count,
                    options,
                    () => new LineGeneratorContext(Environment.Obstructions.Count * 2),
                    (idx, pls, ctx) => {
                        var ls = buffer.Data[idx];
                        GenerateLinesForLightSource(ref ctx, ls);
                        return ctx;
                    },
                    (ctx) => {
                        lock (_ObstructionsByLight)
                            foreach (var kvp in ctx.Queue)
                                _ObstructionsByLight.Add(kvp.Key, kvp.Value);

                        ctx.Dispose();
                    }
                );
            }
#endif
        }

        private void GenerateLinesForLightSource (ref LineGeneratorContext context, LightSource lightSource) {
            Environment.EnumerateObstructionLinesInBounds(lightSource.Bounds, context.LineWriter);

            context.LineWriter.Reset();
            context.Queue.Add(new KeyValuePair<LightSource, DeltaLine[]>(
                lightSource, context.LineWriter.Lines.ToArray()
            ));
        }

        /// <summary>
        /// Computes the amount of light received at a given position in the environment.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="lightIgnorePredicate">A predicate that returns true if a light source should be ignored.</param>
        /// <returns>The total amount of light received at the location (note that the result is not premultiplied, much like LightSource.Color)</returns>
        public Vector4 ComputeReceivedLightAtPosition (Vector2 position, LightIgnorePredicate lightIgnorePredicate = null) {
            var result = Vector4.Zero;

            // FIXME: This enumerates all lights in the scene, which might be more trouble than it's worth.
            // Using the itemboundsenumerator ended up being too expensive due to setup cost.
            foreach (var light in Environment.LightSources) {
                var opacity = light.Opacity;
                if (opacity <= 0f)
                    continue;

                float rampStart = light.RampStart, rampEnd = light.RampEnd;
                var lightPosition = light.Position;

                var deltaFromLight = (position - lightPosition);
                var distanceFromLightSquared = deltaFromLight.LengthSquared();
                if (distanceFromLightSquared > (rampEnd * rampEnd))
                    continue;

                if ((lightIgnorePredicate != null) && lightIgnorePredicate(light))
                    continue;

                var lines = _ObstructionsByLight[light];

                if (FindObstruction(position, lightPosition, lines)) 
                    continue;

                var distanceFromLight = (float)Math.Sqrt(distanceFromLightSquared);

                var distanceScale = 1f - MathHelper.Clamp((distanceFromLight - rampStart) / (rampEnd - rampStart), 0f, 1f);
                if (light.RampMode == LightSourceRampMode.Exponential)
                    distanceScale *= distanceScale;

                // FIXME: Feed distance through ramp texture somehow

                var lightColorScaled = light.Color;
                // Premultiply by alpha here so that things add up correctly. We'll have to reverse this at the end.
                lightColorScaled *= (distanceScale * opacity);

                result += lightColorScaled;
            }

            // Reverse the premultiplication, because we want to match LightSource.Color.
            if (result.W > 0) {
                var unpremultiplyFactor = 1.0f / result.W;
                result.X *= unpremultiplyFactor;
                result.Y *= unpremultiplyFactor;
                result.Z *= unpremultiplyFactor;
            }

            return result;
        }

        private static bool FindObstruction(
            Vector2 startA, Vector2 endA, 
            DeltaLine[] lines
        ) {
            var lengthAX = endA.X - startA.X;
            var lengthAY = endA.Y - startA.Y;

            for (int i = 0, l = lines.Length; i < l; i++) {
                var line = lines[i];

                float q, d, xDelta, yDelta;
                {
                    xDelta = (startA.X - line.X1);
                    yDelta = (startA.Y - line.Y1);

                    q = yDelta * line.LengthX - xDelta * line.LengthY;
                    d = lengthAX * line.LengthY - lengthAY * line.LengthX;
                }

                if (d == 0.0f)
                    continue;

                {
                    d = 1 / d;
                    float r = q * d;

                    if (r < 0.0f || r > 1.0f)
                        continue;

                    {
                        var q2 = yDelta * lengthAX - xDelta * lengthAY;
                        float s = q2 * d;

                        if (s < 0.0f || s > 1.0f)
                            continue;
                    }

                    return true;
                }
            }

            return false;
        }
    }
}
