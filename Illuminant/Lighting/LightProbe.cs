using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant {
    public class LightProbeCollection : IEnumerable<LightProbe> {
        private readonly List<LightProbe> Probes;

        public readonly int MaximumCount;
        public bool IsDirty { get; internal set; }

        public LightProbeCollection (int maximumCount) {
            MaximumCount = maximumCount;
            Probes = new List<LightProbe>(maximumCount);
        }

        public void Add (LightProbe probe) {
            lock (Probes) {
                LightProbeCollection oldParent;
                if ((probe.Collection != null) && probe.Collection.TryGetTarget(out oldParent))
                    throw new InvalidOperationException("Probe already in a collection");

                if (Probes.Count >= MaximumCount)
                    throw new InvalidOperationException("List full");

                probe.Collection = new WeakReference<LightProbeCollection>(this);
                Probes.Add(probe);
                IsDirty = true;
            }
        }

        public void Remove (LightProbe probe) {
            lock (Probes) {
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
        }

        public void Clear () {
            lock (Probes) {
                foreach (var p in Probes)
                    p.Collection = null;
                Probes.Clear();
                IsDirty = true;
            }
        }

        public LightProbe this [int index] {
            get {
                lock (Probes)
                    return Probes[index];
            }
        }

        public int Count {
            get {
                lock (Probes)
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

        internal Vector3 _Position = Vector3.Zero;
        internal Vector3? _Normal = null;
        internal bool _EnableShadows = true;

        public long PreviouslyUpdatedWhen, UpdatedWhen;
        public Vector4 PreviousValue, Value;

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
                if (_Position == value)
                    return;
                _Position = value;
                SetDirty();
            }
        }

        public Vector3? Normal { 
            get {
                return _Normal;
            }
            set {
                if (_Normal == value)
                    return;
                _Normal = value;
                SetDirty();
            }
        }

        public bool EnableShadows {
            get {
                return _EnableShadows;
            }
            set {
                if (_EnableShadows == value)
                    return;
                _EnableShadows = value;
                SetDirty();
            }
        }
    }    

    public class GIProbe {
        public readonly Vector3 Position;

        internal GIProbe (Vector3 position) {
            Position = position;
        }
    }
}
