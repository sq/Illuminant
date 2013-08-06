using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightSource : IHasBounds, ISpatialCollectionChild {
        private readonly HashSet<WeakReference> Parents = new HashSet<WeakReference>();
      
        public object UserData;

        private Vector2 _Position;
        private Bounds _Bounds;
        private float _RampEnd = 1;
        private Texture2D _RampTexture = null;

        internal int _RampTextureID;

        public Vector2 Position {
            get {
                return _Position;
            }
            set {
                _Position = value;
                UpdateBounds();
            }
        }

        public Bounds Bounds {
            get {
                return _Bounds;
            }
        }

        public float RampEnd {
            get {
                return _RampEnd;
            }
            set {
                _RampEnd = value;
                UpdateBounds();
            }
        }

        public Texture2D RampTexture {
            get {
                return _RampTexture;
            }
            set {
                _RampTexture = value;
                if (value == null)
                    _RampTextureID = 0;
                else
                    _RampTextureID = value.GetHashCode();
            }
        }

        public LightSource () {
            UpdateBounds();
        }

        // Controls the blending mode used to render the light source. Additive is usually what you want.
        public LightSourceMode Mode = LightSourceMode.Additive;
        // Controls the nature of the light's distance falloff. Exponential produces falloff that is more realistic (square of distance or whatever) but not necessarily as expected.
        public LightSourceRampMode RampMode = LightSourceRampMode.Linear;
        // The color of the light's illumination.
        // Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        // Alpha is *not* premultiplied (maybe it should be?)
        public Vector4 Color = Vector4.One;
        // The color the light ramps down to. You can use this to approximate ambient light when using LightSourceMode.Max/Min/Replace.
        public Vector4 NeutralColor = Vector4.Zero;
        // The distance from the light source at which the light begins to ramp down.
        public float RampStart = 0;
        // An optional rectangular clipping region to constrain the light source's light.
        public Bounds? ClipRegion = null;
        // A separate opacity factor that you can use to easily fade lights in/out.
        public float Opacity = 1.0f;
        // An optional Nx1 texture used as a lookup table for the light's brightness ramp. You can use this for more precise control over the brightness ramp.
        // FIXME: NOT supported by receivers presently!
        // The filter used when reading from the ramp texture (if one was provided). Linear is ideal if your ramp texture is small, in order to improve quality.
        public TextureFilter RampTextureFilter = TextureFilter.Linear;

        void UpdateBounds () {
            var sz = (Vector2.One * RampEnd);
            _Bounds = new Bounds(
                _Position - sz,
                _Position + sz
            );

            foreach (var wr in Parents) {
                var sc = wr.Target as SpatialCollection<LightSource>;

                if (sc != null)
                    sc.UpdateItemBounds(this);
            }
        }

        public void AddedToCollection (WeakReference collection) {
            Parents.Add(collection);
        }

        public void RemovedFromCollection (WeakReference collection) {
            Parents.Remove(collection);
        }
    }

    public enum LightSourceRampMode {
        Linear,
        Exponential
    }

    public enum LightSourceMode {
        Additive,
        Subtractive,
        Alpha,
        Max,
        Min
    }
}
