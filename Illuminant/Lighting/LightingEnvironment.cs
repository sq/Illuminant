using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightingEnvironment {
        public readonly List<LightSource> Lights = new List<LightSource>();
        // SDF objects that define obstructions to be rendered into the distance field
        public readonly LightObstructionCollection Obstructions = new LightObstructionCollection();
        // Polygonal meshes that define 3D volumes that are rendered into the distance field
        // In 2.5d mode the volumes' top and front faces are also rendered directly into the scene
        public readonly List<HeightVolumeBase> HeightVolumes = new List<HeightVolumeBase>();
        // A set of g-buffer billboards to paint into the distance field and g-buffer.
        // This is an enumerable so that you can map it to existing objects in your game world
        //  instead of maintaining a separate list.
        public IEnumerable<Billboard> Billboards = null;

        // The Z value of the ground plane.
        public float GroundZ = 0f;

        // The Z value of the sky plane. Objects above this will not be represented in the distance field.
        public float MaximumZ = 128f;

        // Offsets Y coordinates by (Z * -ZToYMultiplier) if TwoPointFiveD is enabled
        public float ZToYMultiplier = 0f;

        // Ambient light color
        public Color Ambient = Color.Black;

        public bool EnableGroundShadows = true;

        public void Clear () {
            Lights.Clear();
            Obstructions.Clear();
            HeightVolumes.Clear();
            // FIXME: Set billboards to null?
        }
    }

    public class LightObstructionCollection : IEnumerable<LightObstruction> {
        internal bool IsInvalid = true, IsInvalidDynamic = true;
        internal readonly List<LightObstruction> Items = new List<LightObstruction>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add (LightObstruction value) {
            if (value.IsDynamic)
                IsInvalidDynamic = true;
            else
                IsInvalid = true;
            Items.Add(value);
        }

        public void AddRange (IEnumerable<LightObstruction> items) {
            foreach (var item in items)
                Add(item);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Remove (LightObstruction value) {
            if (value.IsDynamic)
                IsInvalidDynamic = true;
            else
                IsInvalid = true;

            Items.Remove(value);
        }

        public void Clear () {
            IsInvalid = true;
            Items.Clear();
        }

        public int Count {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Items.Count;
            }
        }

        public LightObstruction this [int index] { 
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                return Items[index];
            }
            set {
                var oldValue = Items[index];
                Items[index] = value;
                if (oldValue.IsDynamic || value.IsDynamic)
                    IsInvalidDynamic = true;
                if (!oldValue.IsDynamic || !value.IsDynamic)
                    IsInvalid = true;
            }
        }

        public void CopyTo (LightObstruction[] array) {
            Items.CopyTo(array);
        }

        public List<LightObstruction>.Enumerator GetEnumerator () {
            return Items.GetEnumerator();
        }

        IEnumerator<LightObstruction> IEnumerable<LightObstruction>.GetEnumerator () {
            return ((IEnumerable<LightObstruction>)Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<LightObstruction>)Items).GetEnumerator();
        }
    }
}
