using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public class LightReceiver {
        public readonly HashSet<LightSource> IgnoredLights = new HashSet<LightSource>();

        public Vector2 Position;

        /// <summary>
        /// The additive sum of all light received at this position from all light sources.
        /// Light blocked by obstructions is not included in the computation.
        /// Any light added by rendering outside of the environment (ambient lights, bitmap lights) is not included in the computation.
        /// This property is updated by LightingEnvironment.UpdateReceivers().
        /// </summary>
        public Vector4 ReceivedLight {
            get;
            internal set;
        }

        internal void Update (LightingEnvironment environment) {
            var result = Vector4.Zero;

            // TODO: spatially group light sources so that the receiver update has less work to do? Probably not necessary for low receiver counts.
            foreach (var light in environment.LightSources) {
                var lightColor = light.Color;
                var deltaFromLight = (Position - light.Position);
                var distanceFromLight = deltaFromLight.Length();

                var lightColorScaled = light.Color;
                // Premultiply by alpha here so that things add up correctly. We'll have to reverse this at the end.
                lightColorScaled *= MathHelper.Clamp((distanceFromLight - light.RampStart) / (light.RampEnd - light.RampStart), 0f, 1f);

                // TODO: Skip light sources with an obstruction between the light source and the receiver.

                result += lightColorScaled;
            }

            // Reverse the premultiplication, because we want to match LightSource.LightColor.
            var unpremultiplyFactor = 1.0f / result.W;
            result.X *= unpremultiplyFactor;
            result.Y *= unpremultiplyFactor;
            result.Z *= unpremultiplyFactor;

            ReceivedLight = result;
        }
    }
}
