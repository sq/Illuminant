using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;

namespace Squared.Illuminant {
    public struct Billboard {
        public Texture2D Texture;
        public Bounds    ScreenBounds;
        public Bounds3?  WorldBounds;

        private Vector3? _Normal;
        public Vector3 Normal {
            get {
                return _Normal.GetValueOrDefault(Vector3.UnitY);
            }
            set {
                _Normal = value;
            }
        }

        /// <summary>
        /// Adjusts how strongly the horizontal normals of the billboard will resemble a cylinder.
        /// Set to 0 for a flat billboard and 1 for an approximation of a perfect cylinder.
        /// </summary>
        public float CylinderFactor;
        /// <summary>
        /// For GBufferData billboards, all the texture data is scaled by this amount
        /// For Mask billboards, controls the adjustment of Y coordinates. Set to 0 for no adjustment or 1/null for all pixels to be anchored at the bottom.
        /// </summary>
        public float? DataScale;

        private BillboardType? _Type;
        public BillboardType Type {
            get {
                return _Type.GetValueOrDefault(BillboardType.Mask);
            }
            set {
                _Type = value;
            }
        }

        private Bounds? _TextureBounds;
        /// <summary>
        /// Please note that unlike BitmapDrawCall, this has no influence on the displayed size of the billboard.
        /// </summary>
        public Bounds TextureBounds {
            get {
                if (_TextureBounds.HasValue)
                    return _TextureBounds.Value;

                return Squared.Game.Bounds.Unit;
            }
            set {
                _TextureBounds = value;
            }
        }
    }

    public enum BillboardType {
        /// <summary>
        /// The texture's alpha channel is used as a mask.
        /// Any non-transparent pixels overwrite the g-buffer.
        /// G-buffer values are determined by the billboard's properties.
        /// If no texture is provided the mask is an opaque rectangle.
        /// The Normal property controls the normals generated.
        /// </summary>
        Mask,
        /// <summary>
        /// The texture contains g-buffer data. 
        /// It's EXTREMELY IMPORTANT that this texture not be premultiplied!
        /// Any non-transparent pixels overwrite the g-buffer.
        /// The texture's red channel contains the x-axis normal of the surface, where:
        ///  0.0 is facing left
        ///  0.5 is facing forward
        ///  1.0 is facing right
        /// The texture's green channel contains the y+z normal of the surface, where:
        ///  0.0 is facing up
        ///  0.5 is facing forward
        ///  1.0 is facing down
        /// The texture's blue channel contains the y offset of the pixel,
        ///  where the pixel's y coordinate is offset by (Blue * DataScale)
        /// The new z value for each pixel is determined by a combination of the
        ///  billboard's z position and the y offset of the pixel, as follows:
        ///  Position.Z + ((Blue * DataScale) / ZToYMultiplier)
        /// The Normal property has no effect in this mode.
        /// </summary>
        GBufferData,
    }
}
