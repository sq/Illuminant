using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Illuminant {
    public class Billboard {
        public Texture2D     Texture;
        public Vector3       Position;
        public Vector3       Normal = Vector3.UnitZ;
        public Vector3       Size;
        public bool          CylinderNormals;
        public BillboardType Type = BillboardType.Mask;
        // TODO: Texcoords?
    }

    public enum BillboardType {
        // The texture's alpha channel is used as a mask.
        // Any non-transparent pixels overwrite the g-buffer.
        Mask,
        // The texture contains g-buffer data
        GBufferData
    }
}
