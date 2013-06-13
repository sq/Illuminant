using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightSource : IHasBounds, ISpatialCollectionChild {
        private readonly HashSet<WeakReference> Parents = new HashSet<WeakReference>();

        private Vector2 _Position;
        private Bounds _Bounds;
        private float _RampEnd = 1;

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

        public LightSource () {
            UpdateBounds();
        }

        public LightSourceMode Mode = LightSourceMode.Additive;
        public LightSourceRampMode RampMode = LightSourceRampMode.Linear;
        // The color of the light's illumination.
        // Note that this color is a Vector4 so that you can use HDR (greater than one) lighting values.
        // Alpha is *not* premultiplied (maybe it should be?)
        public Vector4 Color = Vector4.One;
        // The color the light ramps down to. You can use this to approximate ambient light when using LightSourceMode.Max/Min/Replace.
        public Vector4 NeutralColor = Vector4.Zero;
        public float RampStart = 0;
        public Bounds? ClipRegion = null;
        // A separate opacity factor that you can use to easily fade lights in/out.
        public float Opacity = 1.0f;

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
