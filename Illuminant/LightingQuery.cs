﻿using System;
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
        private struct LinesForLightSource : IDisposable {
            public readonly LightSource LightSource;
            public readonly ArraySegment<DeltaLine> Lines; 

            private BufferPool<DeltaLine>.Buffer Buffer;

            static LinesForLightSource () {
                BufferPool<DeltaLine>.MaxPoolCount = 256;
                BufferPool<DeltaLine>.MaxBufferSize = 2048;
            }

            public LinesForLightSource (LightSource lightSource, DeltaLineWriter lineWriter) {
                LightSource = lightSource;
                Buffer = BufferPool<DeltaLine>.Allocate(lineWriter.Lines.Count);

                lineWriter.CopyTo(Buffer.Data, 0, lineWriter.Lines.Count);
                Lines = new ArraySegment<DeltaLine>(Buffer.Data, 0, lineWriter.Lines.Count);
            }

            public void Dispose () {
                Buffer.Dispose();
            }
        }

        private struct DeltaLine {
            public readonly float X1, Y1, Z1;
            public readonly float LengthX, LengthY, LengthZ;

            public DeltaLine (LightPosition a, LightPosition b) {
                X1 = a.X;
                Y1 = a.Y;
                Z1 = a.Z;
                LengthX = b.X - a.X;
                LengthY = b.Y - a.Y;
                LengthZ = b.Z - a.Z;
            }
        }

        private class DeltaLineWriter : ILineWriter {
            public Bounds3? CropBounds;
            public readonly UnorderedList<DeltaLine> Lines;

            public DeltaLineWriter (UnorderedList<DeltaLine> buffer) {
                Lines = buffer;
                CropBounds = null;
            }

            public void Write (LightPosition a, LightPosition b) {
                if (CropBounds.HasValue && Geometry.DoesLineIntersectCube(a, b, CropBounds.Value))
                    return;

                Lines.Add(new DeltaLine(a, b));
            }

            public void CopyTo (DeltaLine[] buffer, int offset, int count) {
                Lines.CopyTo(buffer, offset, count);
            }

            public void Reset () {
                Lines.Clear();
            }
        }

        public readonly LightingEnvironment Environment;

        private readonly Dictionary<LightSource, ArraySegment<DeltaLine>> _ObstructionsByLight =
                new Dictionary<LightSource, ArraySegment<DeltaLine>>(new ReferenceComparer<LightSource>());

        private int IntersectionTestsLastFrame = 0;

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
            public readonly UnorderedList<LinesForLightSource> Queue;

            public LineGeneratorContext (int initialCapacity) {
                LineWriter = new DeltaLineWriter(UnorderedListPool<DeltaLine>.Allocate(initialCapacity));
                Queue = UnorderedListPool<LinesForLightSource>.Allocate();
            }

            public void Dispose () {
                foreach (var lfls in Queue)
                    lfls.Dispose();

                UnorderedListPool<DeltaLine>.Free(LineWriter.Lines);
                UnorderedListPool<LinesForLightSource>.Free(Queue);
            }
        }

        public LightingQuery (LightingEnvironment environment, bool parallelCreate) {
            Environment = environment;

            Update(parallelCreate);
        }

        public void Update (bool parallelUpdate) {
            /*
            if (IntersectionTestsLastFrame > 0)
                Debug.WriteLine("{0} intersection tests last frame", IntersectionTestsLastFrame);
             */

            IntersectionTestsLastFrame = 0;

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
                        _ObstructionsByLight.Add(kvp.LightSource, kvp.Lines);
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
                                _ObstructionsByLight.Add(kvp.LightSource, kvp.Lines);

                        ctx.Dispose();
                    }
                );
            }
#endif
        }

        private void GenerateLinesForLightSource (ref LineGeneratorContext context, LightSource lightSource) {
            context.LineWriter.CropBounds = lightSource.Bounds;

            Environment.EnumerateObstructionLinesInBounds(lightSource.Bounds, context.LineWriter);

            context.LineWriter.CropBounds = null;

            context.Queue.Add(new LinesForLightSource(lightSource, context.LineWriter));
            context.LineWriter.Reset();
        }

        /// <summary>
        /// Computes the amount of light received at a given position in the environment.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="lightIgnorePredicate">A predicate that returns true if a light source should be ignored.</param>
        /// <returns>The total amount of light received at the location (note that the result is not premultiplied, much like LightSource.Color)</returns>
        public bool ComputeReceivedLightAtPosition (LightPosition position, out Vector4 result, LightIgnorePredicate lightIgnorePredicate = null) {
            result = Vector4.Zero;

            // FIXME: This enumerates all lights in the scene, which might be more trouble than it's worth.
            // Using the itemboundsenumerator ended up being too expensive due to setup cost.
            foreach (var light in Environment.LightSources) {
                var opacity = light.Opacity;
                if (opacity <= 0f)
                    continue;

                float rampStart = light.RampStart, rampEnd = light.RampEnd;
                var lightPosition = light.Position;

                var deltaFromLight = (position - (Vector3)lightPosition);
                var distanceFromLightSquared = deltaFromLight.LengthSquared();
                if (distanceFromLightSquared > (rampEnd * rampEnd))
                    continue;

                if ((lightIgnorePredicate != null) && lightIgnorePredicate(light))
                    continue;

                ArraySegment<DeltaLine> lines;
                if (!_ObstructionsByLight.TryGetValue(light, out lines))
                    return false;

                IntersectionTestsLastFrame += lines.Count;

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

            return true;
        }

        private static bool FindObstruction(
            Vector2 startA, Vector2 endA, 
            ArraySegment<DeltaLine> lines
        ) {
            var lengthAX = endA.X - startA.X;
            var lengthAY = endA.Y - startA.Y;

            for (int i = 0, l = lines.Count; i < l; i++) {
                var line = lines.Array[i + lines.Offset];

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
