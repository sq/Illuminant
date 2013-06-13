using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    /// <summary>
    /// Called per light source to determine whether to collect light from that source.
    /// </summary>
    /// <param name="lightSource">The light source.</param>
    /// <returns>True to ignore the light source, false to collect light.</returns>
    public delegate bool LightIgnorePredicate (LightSource lightSource);

    public class LightReceiver {
        public LightIgnorePredicate LightIgnorePredicate;

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
            ReceivedLight = environment.ComputeReceivedLightAtPosition(
                Position, LightIgnorePredicate
            );
        }
    }
}
