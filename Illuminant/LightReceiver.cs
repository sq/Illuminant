using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public class LightReceiver {
        // It's OK to replace this with a shared set used by multiple receivers.
        public HashSet<LightSource> IgnoredLights = new HashSet<LightSource>();

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
                Position, IgnoredLights
            );
        }
    }
}
