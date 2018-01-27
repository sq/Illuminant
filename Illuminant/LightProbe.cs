using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

namespace Squared.Illuminant {
    public class LightProbeCollection : IEnumerable<LightProbe> {
        private readonly List<LightProbe> Probes = new List<LightProbe>();

        public bool IsDirty { get; internal set; }

        public void Add (LightProbe probe) {
            LightProbeCollection oldParent;
            if ((probe.Collection != null) && probe.Collection.TryGetTarget(out oldParent))
                throw new InvalidOperationException("Probe already in a collection");

            probe.Collection = new WeakReference<LightProbeCollection>(this);
            Probes.Add(probe);
            IsDirty = true;
        }

        public void Remove (LightProbe probe) {
            LightProbeCollection oldParent;
            if (
                !probe.Collection.TryGetTarget(out oldParent) || 
                (oldParent != this) ||
                !Probes.Remove(probe)
            )
                throw new InvalidOperationException("Probe not in this collection");

            probe.Collection = null;
            IsDirty = true;
        }

        public void Clear () {
            foreach (var p in Probes)
                p.Collection = null;
            Probes.Clear();
            IsDirty = true;
        }

        public int Count {
            get {
                return Probes.Count;
            }
        }

        public IEnumerator<LightProbe> GetEnumerator () {
            return ((IEnumerable<LightProbe>)Probes).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IEnumerable<LightProbe>)Probes).GetEnumerator();
        }
    }

    public class LightProbe {
        internal WeakReference<LightProbeCollection> Collection = null;

        internal Vector3 _Position = Vector3.Zero, _Normal = Vector3.UnitZ;

        public long PreviouslyUpdatedWhen, UpdatedWhen;
        public Vector3 PreviousValue, Value;

        public object UserData;

        private void SetDirty () {
            if (Collection == null)
                return;

            LightProbeCollection parent;
            if (Collection.TryGetTarget(out parent))
                parent.IsDirty = true;
        }

        public Vector3 Position { 
            get {
                return _Position;
            }
            set {
                _Position = value;
                SetDirty();
            }
        }

        public Vector3 Normal { 
            get {
                return _Normal;
            }
            set {
                _Normal = value;
                SetDirty();
            }
        }
    }
}
