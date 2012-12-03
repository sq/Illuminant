using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;

namespace Squared.Illuminant {
    public class LightSource {
        public LightSourceMode Mode = LightSourceMode.Additive;
        public Vector2 Position;
        // The color of the light's illumination.
        public Vector4 Color;
        // The color the light ramps down to. You can use this to approximate ambient light when using LightSourceMode.Max/Min/Replace.
        public Vector4 NeutralColor;
        public float RampStart, RampEnd;
        public Bounds? ClipRegion;
    }

    public enum LightSourceMode {
        Additive,
        Subtractive,
        Alpha,
        Replace,
        Max,
        Min
    }
}
