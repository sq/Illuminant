using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Render.Tracing;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        internal const int SHValueCount = 9;

        public int GIProbeCount { get; private set; }
        private          RenderTarget2D _SelectedGIProbePositions, _SelectedGIProbeNormals;
        private          RenderTarget2D _GIProbeValues;
        private readonly GIBounce[]     _GIBounces;

        private GIProbeSampleCounts LastGIProbeSampleCount;
        private float   LastGISearchDistance;

        internal int GIProbeSampleCount {
            get {
                return (int)Configuration.GIProbeSampleCount;
            }
        }

        private void _EndGIProbePass (DeviceManager device, object userData) {
            Materials.PopViewTransform();
            device.PopStates();
        }        

        private void _GIProbeBatchSetup (DeviceManager device, object userData) {
            var ltrs = (LightTypeRenderState)userData;

            device.Device.Viewport = new Viewport(0, 0, GIProbeCount, GIProbeSampleCount);
            device.Device.BlendState = RenderStates.AdditiveBlend;

            SetLightShaderParameters(ltrs.ProbeMaterial, ltrs.Key.Quality);

            ltrs.ProbeMaterial.Effect.Parameters["GBuffer"].SetValue(_SelectedGIProbePositions);
            ltrs.ProbeMaterial.Effect.Parameters["GBufferTexelSize"].SetValue(new Vector2(1.0f / _SelectedGIProbePositions.Width, 1.0f / GIProbeSampleCount));
            ltrs.ProbeMaterial.Effect.Parameters["ProbeNormals"].SetValue(_SelectedGIProbeNormals);
            ltrs.ProbeMaterial.Effect.Parameters["RampTexture"].SetValue(ltrs.Key.RampTexture);
        }

        /*
        private GIProbeRadiance? UnpackGIProbeRadiance (
            HalfVector4[] positions, HalfVector4[] normals, HalfVector4[] colors,
            int column, int row, float scaleFactor
        ) {
            var index = (Configuration.MaximumGIProbeCount * row) + column;
            var pos = positions[index].ToVector4();
            var norm = normals[index].ToVector4();

            if ((pos.W < 1) || (norm.W < 1))
                return null;

            return new GIProbeRadiance {
                Position = new Vector3(pos.X, pos.Y, pos.Z),
                SurfaceNormal = new Vector3(norm.X, norm.Y, norm.Z),
                Value = colors[index].ToVector4() * scaleFactor
            };
        }
        */

        private void ReleaseGIProbeResources () {
            Coordinator.DisposeResource(_SelectedGIProbePositions);
            Coordinator.DisposeResource(_SelectedGIProbeNormals);
            Coordinator.DisposeResource(_GIProbeValues);

            foreach (var b in _GIBounces) {
                if (b != null)
                    Coordinator.DisposeResource(b.SH);
            }
        }

        private void CreateGIProbeResources () {
            ReleaseGIProbeResources();

            if (Environment != null)
                Environment.GIVolumes.IsDirty = true;

            _SelectedGIProbePositions = new RenderTarget2D(
                Coordinator.Device, Configuration.MaximumGIProbeCount, GIProbeSampleCount, false,
                SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents
            );

            _SelectedGIProbeNormals = new RenderTarget2D(
                Coordinator.Device, Configuration.MaximumGIProbeCount, GIProbeSampleCount, false,
                SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents
            );

            _GIProbeValues = new RenderTarget2D(
                Coordinator.Device, Configuration.MaximumGIProbeCount, GIProbeSampleCount, false,
                SurfaceFormat.HdrBlendable, DepthFormat.None, 0, RenderTargetUsage.PreserveContents
            );
                
            for (int i = 0; i < _GIBounces.Length; i++)
                _GIBounces[i] = new GIBounce(this, i);

            LastGIProbeSampleCount = Configuration.GIProbeSampleCount;
        }

        private void UpdateGIProbes (IBatchContainer container, int layer, float intensityScale) {
            if (!Configuration.EnableGlobalIllumination)
                return;

            if (LastGIProbeSampleCount != Configuration.GIProbeSampleCount)
                CreateGIProbeResources();

            using (var group = BatchGroup.New(container, layer, null, (dm, _) => {
                for (int i = 0; i < 16; i++)
                    dm.Device.Textures[i] = null;
            })) {
                if (
                    (LastGISearchDistance != Configuration.GIBounceSearchDistance)
                ) {
                    Environment.GIVolumes.IsDirty = true;
                    LastGISearchDistance = Configuration.GIBounceSearchDistance;
                }

                if (Environment.GIVolumes.IsDirty)
                    SelectGIProbes(group, 0);

                int bounceLayer = 1;
                for (int i = 0; i < Math.Min(Configuration.MaximumGIBounceCount, Configuration.MaximumGIUpdatesPerFrame); i++)
                    UpdateOldestGIBounce(group, ref bounceLayer, intensityScale);
            }
        }

        private void UpdateOldestGIBounce (IBatchContainer container, ref int layer, float intensityScale) {
            GIBounce bounce = null;
            long lowestTimestamp = long.MaxValue;

            for (int i = 0; i < Configuration.MaximumGIBounceCount; i++) {
                var b = _GIBounces[i];
                if (b.Timestamp < lowestTimestamp) {
                    bounce = b;
                    lowestTimestamp = b.Timestamp;
                }
            }

            if (GIProbeCount > 0) {
                if (bounce.Index == 0) {
                    UpdateLightProbes(container, layer++, _GIProbeValues, true, intensityScale);
                    UpdateGIProbeSH(container, layer++, 0, intensityScale);
                } else {
                    UpdateLightProbesFromGI(container, layer++, _GIProbeValues, bounce.Index - 1);
                    UpdateGIProbeSH(container, layer++, bounce.Index, 1);
                }
            }

            bounce.Timestamp = Time.Ticks;
            bounce.IsValid = true;
        }
        
        private void MakeGIVolumeVertices (
            GIProbeVertex[] buf, ref int offset, 
            RenderTarget2D renderTarget, GIVolume volume, bool screenSpace
        ) {
            Vector4 offsetAndBaseIndex, intervalAndCount;

            if (volume != null) {
                offsetAndBaseIndex = new Vector4(volume.ProbeOffset, volume.BaseIndex) + 
                    new Vector4(volume.Bounds.TopLeft, 0, 0);
                intervalAndCount = new Vector4(
                    volume.ProbeInterval.X,
                    volume.ProbeInterval.Y,
                    volume.ProbeCountX,
                    volume.ProbeCountY
                );
            } else {
                offsetAndBaseIndex = intervalAndCount = Vector4.Zero;
            }

            float x1, x2, y1, y2;

            if (volume == null) {
                x1 = -1;
                x2 = 1;
                y1 = -1;
                y2 = 1;
            } else if (screenSpace) {
                x1 = volume.Bounds.TopLeft.X;
                y1 = volume.Bounds.TopLeft.Y;
                x2 = volume.Bounds.BottomRight.X;
                y2 = volume.Bounds.BottomRight.Y;
            } else {
                double div = 1.0 / GIProbeCount;
                x1 = (float)((double)volume.BaseIndex * div * 2) - 1;
                x2 = (float)((double)(volume.BaseIndex + volume.ProbeCount) * div * 2) - 1;
                y1 = -1;
                y2 = 1;
            }

            buf[offset + 0] = new GIProbeVertex(x1, y1, offsetAndBaseIndex, intervalAndCount);
            buf[offset + 1] = new GIProbeVertex(x2, y1, offsetAndBaseIndex, intervalAndCount);
            buf[offset + 2] = new GIProbeVertex(x2, y2, offsetAndBaseIndex, intervalAndCount);
            buf[offset + 3] = new GIProbeVertex(x1, y2, offsetAndBaseIndex, intervalAndCount);
            offset += 4;
        }

        private void SelectGIProbes (IBatchContainer container, int layer) {
            var m = IlluminantMaterials.GIProbeSelector;
            var p = m.Effect.Parameters;

            if (
                (Environment.GIVolumes.Count < 1) ||
                (_DistanceField == null)
            ) {
                GIProbeCount = 0;
                return;
            }

            var extent = Extent3;

            GIProbeCount = 0;
            foreach (var v in Environment.GIVolumes) {
                if (!v.IsValid)
                    continue;

                v.BaseIndex = GIProbeCount;
                v.UpdateCount();
                GIProbeCount += v.ProbeCount;
            }

            if (GIProbeCount > Configuration.MaximumGIProbeCount)
                GIProbeCount = Configuration.MaximumGIProbeCount;

            if (GIProbeCount < 1)
                return;

            using (var rt = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    dm.PushRenderTargets(new RenderTargetBinding[] { _SelectedGIProbePositions, _SelectedGIProbeNormals });
                    dm.Device.Viewport = new Viewport(0, 0, GIProbeCount, GIProbeSampleCount);

                    SetDistanceFieldParameters(m, true, Configuration.GIProbeQuality);

                    p["NormalCount"].SetValue(GIProbeSampleCount);
                    p["BounceSearchDistance"].SetValue(Configuration.GIBounceSearchDistance);

                    m.Flush();
                },
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                    dm.PopRenderTarget();

                    // HACK: If probes are selected while the distance field is only partially generated, 
                    //  they will potentially penetrate walls. As a result we can't preserve those probes
                    //  and should regenerate them at the next opportunity.
                    // If the distance field is invalidated too frequently this will result in the GI
                    //  probes always staying dirty, which may not be what you want.
                    if (
                        _DistanceField.IsFullyGenerated &&
                        Configuration.GICaching
                    )
                        Environment.GIVolumes.IsDirty = false;
                }
            )) {
                RenderTrace.Marker(rt, 0, "Select GI probe locations");

                RenderGIVolumes(rt, 1, m, false);
            }
        }

        private void RenderGIVolumes (IBatchContainer container, int layer, Material material, bool screenSpace) {
            var buf = new GIProbeVertex[6 * Environment.GIVolumes.Count];
            int j = 0;

            using (var pb = PrimitiveBatch<GIProbeVertex>.New(
                container, layer, material
            ))
            for (int i = 0; i < Environment.GIVolumes.Count; i++) {
                var volume = Environment.GIVolumes[i];
                if (!volume.IsValid || !volume.Visible)
                    continue;

                var pdc = new PrimitiveDrawCall<GIProbeVertex>(
                    PrimitiveType.TriangleList, buf, j, 6, QuadIndices, 0, 2
                );
                pb.Add(ref pdc);

                MakeGIVolumeVertices(buf, ref j, _SelectedGIProbePositions, volume, screenSpace);
            }
        }

        private void UpdateLightProbesFromGI (IBatchContainer container, int layer, RenderTarget2D renderTarget, int sourceBounceIndex) {
            var source = _GIBounces[sourceBounceIndex].SH;
            var m = IlluminantMaterials.RenderLightProbesFromGI;
            var p = m.Effect.Parameters;

            if (GIProbeCount < 1)
                return;

            using (var group = BatchGroup.ForRenderTarget(
                container, layer, renderTarget,
                (dm, _) => {
                    dm.Device.Viewport = new Viewport(0, 0, GIProbeCount, GIProbeSampleCount);
                    dm.Device.BlendState = RenderStates.AdditiveBlend;

                    SetLightShaderParameters(m, Configuration.GIProbeQuality);

                    p["Brightness"].SetValue(1.0f);

                    p.ClearTextures("GBuffer", "SphericalHarmonics", "ProbeNormals");

                    p["GBuffer"].SetValue(_SelectedGIProbePositions);
                    p["GBufferTexelSize"].SetValue(new Vector2(1.0f / _SelectedGIProbePositions.Width, 1.0f / GIProbeSampleCount));
                    p["ProbeNormals"].SetValue(_SelectedGIProbeNormals);

                    p["SphericalHarmonicsTexelSize"].SetValue(new Vector2(1.0f / source.Width, 1.0f / source.Height));
                    p["SphericalHarmonics"].SetValue(source);

                    m.Flush();
                },
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                RenderTrace.Marker(group, 0, "Update GI probes from bounce {0}", sourceBounceIndex);

                ClearBatch.AddNew(
                    group, 1, Materials.Clear, Color.Transparent
                );

                RenderGIVolumes(group, 2, m, false);
            }
        }

        private void UpdateGIProbeSH (IBatchContainer container, int layer, int bounceIndex, float intensityScale) {
            if (GIProbeCount < 1)
                return;

            var m = IlluminantMaterials.GIProbeSHGenerator;
            var p = m.Effect.Parameters;

            var previousBounce = (bounceIndex > 0) ? _GIBounces[bounceIndex - 1].SH : null;
            var bounce = _GIBounces[bounceIndex].SH;

            using (var rt = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    dm.PushRenderTarget(bounce);
                    dm.Device.Viewport = new Viewport(0, 0, GIProbeCount, SHValueCount);
                    dm.Device.BlendState = BlendState.Opaque;

                    p["Brightness"].SetValue(1.0f + (Configuration.GIBounceBrightnessAmplification * bounceIndex));

                    p.ClearTextures("ProbeValues", "PreviousBounce");

                    p["InverseScaleFactor"].SetValue(1.0f / intensityScale);
                    p["NormalCount"].SetValue(GIProbeSampleCount);
                    p["ProbeValuesTexelSize"].SetValue(new Vector2(1.0f / _GIProbeValues.Width, 1.0f / _GIProbeValues.Height));
                    p["ProbeValues"].SetValue(_GIProbeValues);
                    p["PreviousBounceTexelSize"].SetValue(
                        (previousBounce != null)
                            ? new Vector2(1.0f / previousBounce.Width, 1.0f / previousBounce.Height)
                            : Vector2.Zero
                    );
                    p["PreviousBounce"].SetValue(previousBounce);

                    m.Flush();
                },
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                    dm.PopRenderTarget();
                }
            )) {
                RenderTrace.Marker(rt, 0, "Update spherical harmonics for bounce {0}", bounceIndex);

                ClearBatch.AddNew(rt, 1, Materials.Clear, clearColor: Color.Black);

                using (var pb = PrimitiveBatch<GIProbeVertex>.New(
                    rt, 2, m
                )) {
                    var buf = new GIProbeVertex[4];
                    int temp = 0;
                    MakeGIVolumeVertices(buf, ref temp, _SelectedGIProbePositions, null, true);

                    var pdc = new PrimitiveDrawCall<GIProbeVertex>(
                        PrimitiveType.TriangleList,
                        buf, 0, 4, QuadIndices, 0, 2
                    );
                    pb.Add(ref pdc);
                }
            }
        }

        public void VisualizeGIProbes (IBatchContainer container, int layer, float radius, int bounceIndex = 0, float brightness = 1) {
            var m = Materials.Get(IlluminantMaterials.VisualizeGI, blendState: BlendState.AlphaBlend);
            var p = m.Effect.Parameters;
            var source = _GIBounces[bounceIndex].SH;

            using (var group = BatchGroup.New(
                container, layer,
                null,
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            ))
            using (var pb = PrimitiveBatch<VisualizeGIProbeVertex>.New(
                group, 1, m,
                (dm, _) => {
                    p["Brightness"].SetValue(brightness);
                    p["SphericalHarmonicsTexelSize"].SetValue(new Vector2(1.0f / source.Width, 1.0f / source.Height));
                    p.ClearTexture("SphericalHarmonics");
                    p["SphericalHarmonics"].SetValue(source);

                    m.Flush();
                }
            )) {
                RenderTrace.Marker(group, 0, "Visualize GI probes");

                var buf = new VisualizeGIProbeVertex[GIProbeCount * 6];
                int k = 0;

                foreach (var v in Environment.GIVolumes) {
                    if (!v.IsValid)
                        continue;
                    if (!v.Visible)
                        continue;

                    for (int j = 0; j < v.ProbeCount; j++) {
                        int y = j / v.ProbeCountX;
                        int x = j - (y * v.ProbeCountX);
                        var idx = (short)(v.BaseIndex + j);
                        var pos = new Vector3(v.Bounds.TopLeft, 0) + 
                            v.ProbeOffset + 
                            new Vector3(v.ProbeInterval.X * x, v.ProbeInterval.Y * y, 0);
                        buf[k + 0] = new VisualizeGIProbeVertex(pos, -1, -1, idx, radius); // 0
                        buf[k + 1] = buf[k + 3] = new VisualizeGIProbeVertex(pos, 1, -1, idx, radius); // 1
                        buf[k + 4] = new VisualizeGIProbeVertex(pos, 1, 1, idx, radius); // 2
                        buf[k + 2] = buf[k + 5] = new VisualizeGIProbeVertex(pos, -1, 1, idx, radius); // 3
                        k += 6;
                    }
                }

                if (k != 0) {
                    var pdc = new PrimitiveDrawCall<VisualizeGIProbeVertex>(
                        PrimitiveType.TriangleList,
                        buf, 0, k / 3
                    );
                    pb.Add(ref pdc);
                }
            }
        }

        internal void RenderGlobalIllumination (
            IBatchContainer container, int layer, float brightness, int bounceIndex, float intensityScale
        ) {
            var m = IlluminantMaterials.RenderGI;
            var p = m.Effect.Parameters;
            RenderTarget2D source = null;

            for (int i = 0; i <= bounceIndex; i++) {
                var bounce = _GIBounces[bounceIndex];
                if (!bounce.IsValid)
                    continue;

                source = bounce.SH;
            }

            if (source == null)
                return;

            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    dm.Device.BlendState = Configuration.GIBlendMode;

                    SetLightShaderParameters(m, Configuration.GIProbeQuality);

                    p["Brightness"].SetValue(brightness * intensityScale);
                    p["SphericalHarmonicsTexelSize"].SetValue(new Vector2(1.0f / Configuration.MaximumGIProbeCount, 1.0f / SHValueCount));
                    p["SphericalHarmonics"].SetValue((Texture2D)null);
                    p["SphericalHarmonics"].SetValue(source);

                    m.Flush();
                },
                (dm, _) => {
                    for (int i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                RenderTrace.Marker(group, 0, "Render GI from bounce {0}", bounceIndex);

                RenderGIVolumes(group, 1, m, true);
            }
        }
    }

    public enum GIProbeSampleCounts : int {
        Low = 32,
        Medium = 48,
        High = 64,
        Excessive = 96,
        Absurd = 128
    }

    public class GIRenderSettings {
        public float Brightness = 1;
        public int   BounceIndex = 0;
    }

    public class GIVolume {
        public GIVolumeCollection Collection { get; internal set; }

        internal bool IsDirty = true;

        internal Bounds _Bounds;
        internal Vector3 _ProbeOffset;
        internal Vector2 _ProbeInterval;

        internal int BaseIndex;
        internal int ProbeCount;
        internal int ProbeCountX;
        internal int ProbeCountY;

        public bool Visible = true;

        internal void UpdateCount () {
            ProbeCountX = (int)Math.Ceiling((Bounds.Size.X - ProbeOffset.X) / ProbeInterval.X);
            ProbeCountY = (int)Math.Ceiling((Bounds.Size.Y - ProbeOffset.Y) / ProbeInterval.Y);
            ProbeCount = ProbeCountX * ProbeCountY;
        }

        public bool IsValid {
            get {
                return (Bounds.Size.X > 0) && (Bounds.Size.Y > 0) &&
                    (_ProbeInterval.X > 0) && (_ProbeInterval.Y > 0);
            }
        }

        public Bounds Bounds {
            get {
                return _Bounds;
            }
            set {
                if (value.Equals(_Bounds))
                    return;

                if (Collection != null)
                    Collection.IsDirty = true;

                IsDirty = true;
                _Bounds = value;
            }
        }

        public Vector3 ProbeOffset {
            get {
                return _ProbeOffset;
            }
            set {
                if (value == _ProbeOffset)
                    return;

                if (Collection != null)
                    Collection.IsDirty = true;

                IsDirty = true;
                _ProbeOffset = value;
            }
        }

        public Vector2 ProbeInterval {
            get {
                return _ProbeInterval;
            }
            set {
                if (value == _ProbeInterval)
                    return;

                if (Collection != null)
                    Collection.IsDirty = true;

                IsDirty = true;
                _ProbeInterval = value;
            }
        }
    }

    public class GIVolumeCollection : IEnumerable<GIVolume> {
        internal readonly List<GIVolume> Volumes = new List<GIVolume>();
        internal bool IsDirty = true;

        public GIVolume this[int index] {
            get {
                return Volumes[index];
            }
        }

        public int Count {
            get {
                return Volumes.Count;
            }
        }

        public void Add (GIVolume item) {
            if (item.Collection != null)
                throw new InvalidOperationException("Already in another volume collection");

            item.Collection = this;
            Volumes.Add(item);
            IsDirty = true;
        }

        public void Clear () {
            foreach (var v in Volumes)
                v.Collection = null;

            Volumes.Clear();
            IsDirty = true;
        }

        public bool Contains (GIVolume item) {
            return Volumes.Contains(item);
        }

        public IEnumerator<GIVolume> GetEnumerator () {
            return ((IList<GIVolume>)Volumes).GetEnumerator();
        }

        public bool Remove (GIVolume item) {
            if (item.Collection != this)
                throw new InvalidOperationException("Not in this collection");

            item.Collection = null;
            IsDirty = true;
            return Volumes.Remove(item);
        }

        public void RemoveAt (int index) {
            var item = Volumes[index];
            item.Collection = null;
            IsDirty = true;
            Volumes.RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return ((IList<GIVolume>)Volumes).GetEnumerator();
        }
    }

    internal class GIBounce {
        public readonly RenderTarget2D SH;
        public readonly int Index;
        public long Timestamp;
        public bool IsValid;

        public GIBounce (LightingRenderer renderer, int index) {
            Index = index;
            SH = new RenderTarget2D(
                renderer.Coordinator.Device, renderer.Configuration.MaximumGIProbeCount, LightingRenderer.SHValueCount, false,
                SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.PreserveContents
            );
        }

        public void Invalidate () {
            Timestamp = 0;
            IsValid = false;
        }
    }
}
