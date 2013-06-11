using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingEnvironment {
        // If you have very detailed obstruction geometry, set this lower to reduce GPU load.
        // For coarse geometry you might want to set this higher to reduce CPU load and memory usage.
        public static float DefaultSubdivision = 128f;

        public readonly List<LightSource> LightSources = new List<LightSource>();
        public readonly List<LightReceiver> LightReceivers = new List<LightReceiver>();
        public readonly SpatialCollection<LightObstructionBase> Obstructions = new SpatialCollection<LightObstructionBase>(DefaultSubdivision);

        public void EnumerateObstructionLinesInBounds (Bounds bounds, ILineWriter output) {
            SpatialCollection<LightObstructionBase>.ItemInfo ii;

            using (var e = Obstructions.GetItemsFromBounds(bounds, false))
            while (e.GetNext(out ii))
                ii.Item.GenerateLines(output);
        }

        /// <param name="position">The position.</param>
        /// <param name="ignoredLights">A set of lights to ignore, if any. If this value is a HashSet of LightSources it will be used directly, otherwise the sequence is copied.</param>
        /// <returns>The created receiver</returns>
        public LightReceiver AddLightReceiver (Vector2 position, IEnumerable<LightSource> ignoredLights = null) {
            var result = new LightReceiver {
                Position = position
            };

            var hs = ignoredLights as HashSet<LightSource>;
            if (hs != null)
                result.IgnoredLights = hs;
            else if (ignoredLights != null)
                result.IgnoredLights = new HashSet<LightSource>(ignoredLights);

            LightReceivers.Add(result);

            return result;
        }

        /// <summary>
        /// Updates all the lighting environment's receivers based on the current positions of light sources and obstructions.
        /// </summary>
        public void UpdateReceivers () {
            foreach (var receiver in LightReceivers)
                receiver.Update(this);
        }

        /// <summary>
        /// Computes the amount of light received at a given position in the environment.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="ignoredLights">A set of lights, if any, that should be ignored when computing received light. Useful if you want to measure incoming light at the position of a light source or ignore certain light sources.</param>
        /// <returns>The total amount of light received at the location (note that the result is not premultiplied, much like LightSource.Color)</returns>
        public Vector4 ComputeReceivedLightAtPosition (Vector2 position, HashSet<LightSource> ignoredLights = null) {
            var result = Vector4.Zero;

            // TODO: spatially group light sources so that the receiver update has less work to do? Probably not necessary for low receiver counts.
            foreach (var light in LightSources) {
                if ((ignoredLights != null) && ignoredLights.Contains(light))
                    continue;

                var lightColor = light.Color;
                var deltaFromLight = (position - light.Position);
                var distanceFromLight = deltaFromLight.Length();
                var distanceScale = 1f - MathHelper.Clamp((distanceFromLight - light.RampStart) / (light.RampEnd - light.RampStart), 0f, 1f);

                var lightColorScaled = light.Color;
                // Premultiply by alpha here so that things add up correctly. We'll have to reverse this at the end.
                lightColorScaled *= distanceScale;

                // TODO: Skip light sources with an obstruction between the light source and the receiver.

                result += lightColorScaled;
            }

            // Reverse the premultiplication, because we want to match LightSource.Color.
            var unpremultiplyFactor = 1.0f / result.W;
            result.X *= unpremultiplyFactor;
            result.Y *= unpremultiplyFactor;
            result.Z *= unpremultiplyFactor;

            return result;
        }
    }

    public class CroppedListLineWriter : ILineWriter {
        public struct Line {
            public readonly Vector2 A, B;

            public Line (Vector2 a, Vector2 b) {
                A = a; 
                B = b;
            }
        }

        public Bounds? CropBounds;
        public readonly UnorderedList<Line> Lines = new UnorderedList<Line>();

        public void Write (Vector2 a, Vector2 b) {
            if (CropBounds.HasValue) {
                // constructor doesn't get inlined here :(
                Bounds lineBounds;
                lineBounds.TopLeft.X = Math.Min(a.X, b.X);
                lineBounds.TopLeft.Y = Math.Min(a.Y, b.Y);
                lineBounds.BottomRight.X = Math.Max(a.X, b.X);
                lineBounds.BottomRight.Y = Math.Max(a.Y, b.Y);

                if (!CropBounds.Value.Intersects(lineBounds))
                    return;
            }

            Lines.Add(new Line(a, b));
        }

        public void Reset () {
            Lines.Clear();
        }
    }
}
