using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public class LightingQuery {
        public readonly LightingEnvironment Environment;

        private readonly ThreadLocal<CroppedListLineWriter> _LineWriter = new ThreadLocal<CroppedListLineWriter>(
            () => 
                new CroppedListLineWriter()
        );

        private readonly ThreadLocal<Dictionary<LightSource, CroppedListLineWriter.Line[]>> _ObstructionsByLight = new ThreadLocal<Dictionary<LightSource, CroppedListLineWriter.Line[]>>(
            () => 
                new Dictionary<LightSource, CroppedListLineWriter.Line[]>(new ReferenceComparer<LightSource>())
        );

        public LightingQuery (LightingEnvironment environment) {
            Environment = environment;
        }

        private CroppedListLineWriter.Line[] GetObstructionsForLightSource (LightSource light) {
            CroppedListLineWriter.Line[] result;

            var obl = _ObstructionsByLight.Value;

            if (!obl.TryGetValue(light, out result)) {
                var lw = _LineWriter.Value;

                lw.Reset();
                lw.CropBounds = light.Bounds;

                Environment.EnumerateObstructionLinesInBounds(light.Bounds, lw);

                result = obl[light] = lw.Lines.ToArray();

                lw.Reset();
            }

            return result;
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

                var lines = GetObstructionsForLightSource(light);

                bool foundIntersection = false;

                foreach (var line in lines) {
                    foundIntersection |= Geometry.DoLinesIntersect(line.A, line.B, lightPosition, position);
                    if (foundIntersection)
                        break;
                }

                if (foundIntersection)
                    continue;

                var distanceFromLight = (float)Math.Sqrt(distanceFromLightSquared);

                var distanceScale = 1f - MathHelper.Clamp((distanceFromLight - rampStart) / (rampEnd - rampStart), 0f, 1f);
                if (light.RampMode == LightSourceRampMode.Exponential)
                    distanceScale *= distanceScale;

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
    }
}
