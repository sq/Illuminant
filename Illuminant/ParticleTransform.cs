using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render;

namespace Squared.Illuminant.Transforms {
    public abstract class ParticleTransform {
        internal abstract Material GetMaterial (ParticleMaterials materials);
        internal abstract void SetParameters (EffectParameterCollection parameters);
    }

    public abstract class ParticleFMA : ParticleTransform {
        public Vector3 Add, Multiply;

        internal override void SetParameters (EffectParameterCollection parameters) {
            parameters["Add"].SetValue(Add);
            parameters["Multiply"].SetValue(Multiply);
        }
    }

    public class PositionFMA : ParticleFMA {
        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.PositionFMA;
        }
    }

    public class VelocityFMA : ParticleFMA {
        internal override Material GetMaterial (ParticleMaterials materials) {
            return materials.VelocityFMA;
        }
    }
}
