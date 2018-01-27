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
        private readonly List<LightProbe> Probes = new List<LightProbe>();

        public bool IsDirty { get; internal set; }

        public void Add (LightProbe probe) {
            lock (Probes) {
                LightProbeCollection oldParent;
                if ((probe.Collection != null) && probe.Collection.TryGetTarget(out oldParent))
                    throw new InvalidOperationException("Probe already in a collection");

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
                _Position = value;
                SetDirty();
            }
        }

        public Vector3? Normal { 
            get {
                return _Normal;
            }
            set {
                _Normal = value;
                SetDirty();
            }
        }
    }

    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private struct LightProbeDownloadTask : IWorkItem {
            public LightingRenderer Renderer;
            public RenderTarget2D Texture;
            public long Timestamp;
            public float ScaleFactor;

            public void Execute () {
                var count = Renderer.Probes.Count;
                var now = Time.Ticks;

                lock (Renderer._LightProbeReadbackArrayLock) {
                    var buffer = Renderer._LightProbeReadbackArray;
                    if ((buffer == null) || (buffer.Length < (count)))
                        buffer = Renderer._LightProbeReadbackArray = new HalfVector4[count];

                    lock (Renderer.Coordinator.UseResourceLock)
                        Texture.GetData(
                            0, new Rectangle(0, 0, count, 1),
                            buffer, 0, count
                        );

                    int i = 0;

                    lock (Renderer.Probes)
                    foreach (var p in Renderer.Probes) {
                        if (p.UpdatedWhen >= Timestamp) {
                            i++;
                            continue;
                        }

                        p.PreviouslyUpdatedWhen = p.UpdatedWhen;
                        p.PreviousValue = p.Value;
                        p.UpdatedWhen = now;
                        p.Value = buffer[i++].ToVector4() * ScaleFactor;
                    }
                }

                return;
            }
        }
    }
}
