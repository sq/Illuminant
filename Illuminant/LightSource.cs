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
        // Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        // Alpha is *not* premultiplied (maybe it should be?)
        public Vector4 Color = Vector4.One;
        // The color the light ramps down to. You can use this to approximate ambient light when using LightSourceMode.Max/Min/Replace.
        public Vector4 NeutralColor = Vector4.Zero;
        public float RampStart = 0, RampEnd = 1;
        public Bounds? ClipRegion = null;
        // A separate opacity factor that you can use to easily fade lights in/out.
        public float Opacity = 1.0f;
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
