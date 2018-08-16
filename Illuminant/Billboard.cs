using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;

namespace Squared.Illuminant {
    public struct Billboard {
        public Texture2D Texture;
        public Vector3   Position;

        private Vector3? _Normal;
        public Vector3 Normal {
            get {
                return _Normal.GetValueOrDefault(Vector3.UnitZ);
            }
            set {
                _Normal = value;
            }
        }

        public Vector3 Size;
        public bool    CylinderNormals;
        // Manipulates GBufferData billboards
        public float   DataScale;

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
        public Bounds TextureBounds {
            get {
                if (_TextureBounds.HasValue)
                    return _TextureBounds.Value;

                return Bounds.Unit;
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
        /// </summary>
        GBufferData
    }
}
