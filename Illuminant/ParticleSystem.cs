﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;
using Squared.Util;

namespace Squared.Illuminant {
    public class ParticleSystem : IDisposable {
        internal class LivenessInfo : IDisposable {
            /*
            public RenderTarget2D Buffer;
            public byte[]         Flags;
            */
            public uint           Count;
            public OcclusionQuery Query;
            public long           LastQueryStart;

            public void Dispose () {
                Query.Dispose();
                Query = null;
                /*
                Buffer.Dispose();
                Buffer = null;
                Flags = null;
                */
            }
        }

        private class Slice : IDisposable {
            public class Chunk : IDisposable {
                public const int Width = 256;
                public const int Height = 256;
                public const int MaximumCount = Width * Height;

                public int Index;

                public RenderTargetBinding[] Bindings;

                public RenderTarget2D PositionAndBirthTime;
                public RenderTarget2D Velocity;
                public RenderTarget2D Attributes;

                public Chunk (
                    int attributeCount, GraphicsDevice device
                ) {
                    Index = -1;

                    Bindings = new RenderTargetBinding[2 + attributeCount];
                    Bindings[0] = PositionAndBirthTime = CreateRenderTarget(device);
                    Bindings[1] = Velocity = CreateRenderTarget(device);

                    if (attributeCount == 1)
                        Bindings[2] = Attributes = CreateRenderTarget(device);
                }

                private RenderTarget2D CreateRenderTarget (GraphicsDevice device) {
                    return new RenderTarget2D(
                        device, 
                        Width, Height, false, 
                        SurfaceFormat.Vector4, DepthFormat.None, 
                        0, RenderTargetUsage.PreserveContents
                    );
                }

                // Make sure to lock the slice first.
                public void Initialize<TAttribute> (
                    int offset,
                    Action<Vector4[], int> positionInitializer,
                    Action<Vector4[], int> velocityInitializer,
                    Action<TAttribute[], int> attributeInitializer
                ) where TAttribute : struct {
                    var buf = new Vector4[MaximumCount];

                    if (positionInitializer != null) {
                        positionInitializer(buf, offset);
                        PositionAndBirthTime.SetData(buf);
                    }

                    if (velocityInitializer != null) {
                        velocityInitializer(buf, offset);
                        Velocity.SetData(buf);
                    }

                    if ((attributeInitializer != null) && (Attributes != null)) {
                        TAttribute[] abuf;
                        if (typeof(TAttribute) == typeof(Vector4))
                            abuf = buf as TAttribute[];
                        else
                            abuf = new TAttribute[MaximumCount];

                        attributeInitializer(abuf, offset);
                        Attributes.SetData(abuf);
                    }
                }

                public void Dispose () {
                    PositionAndBirthTime.Dispose();
                    Velocity.Dispose();
                    if (Attributes != null)
                        Attributes.Dispose();

                    PositionAndBirthTime = Velocity = Attributes = null;
                }
            }

            public readonly int Index;
            public readonly int AttributeCount;
            public long Timestamp;
            public bool IsValid, IsBeingGenerated;
            public int  InUseCount;

            private readonly List<Chunk> Chunks = new List<Chunk>();

            public Slice (
                GraphicsDevice device, int index, int attributeCount
            ) {
                Index = index;
                AttributeCount = attributeCount;
                if ((attributeCount > 1) || (attributeCount < 0))
                    throw new ArgumentException("Valid attribute counts are 0 and 1");
                Timestamp = Squared.Util.Time.Ticks;
            }

            public void Lock (string reason) {
                // Console.WriteLine("Lock {0} for {1}", Index, reason);
                lock (this)
                    InUseCount++;
            }

            public void Unlock () {
                // Console.WriteLine("Unlock {0}", Index);
                lock (this)
                    InUseCount--;
            }

            public void Dispose () {
                IsValid = false;

                lock (Chunks)
                foreach (var c in Chunks)
                    c.Dispose();
            }            

            public Chunk RemoveAt (int index) {
                lock (Chunks) {
                    var chunk = Chunks[index];
                    Chunks.RemoveAt(index);
                    return chunk;
                }
            }

            public void Add (Chunk chunk) {
                lock (Chunks)
                    Chunks.Add(chunk);
            }

            public Chunk this [int index] {
                get {
                    lock (Chunks)
                        return Chunks[index];
                }
            }

            public int Count {
                get {
                    lock (Chunks)
                        return Chunks.Count;
                }
            }
        }

        public          int LiveCount { get; private set; }

        public readonly ParticleEngine                     Engine;
        public readonly ParticleSystemConfiguration        Configuration;
        public readonly List<Transforms.ParticleTransform> Transforms = 
            new List<Illuminant.Transforms.ParticleTransform>();

        // 3 because we go
        // old -> a -> b -> a -> ... -> done
        private const int SliceCount          = 3;
        private Slice[] Slices;

        private readonly List<Slice.Chunk> NewChunks = new List<Slice.Chunk>();

        private readonly List<LivenessInfo> LivenessInfos = new List<LivenessInfo>();

        private readonly IndexBuffer  QuadIndexBuffer;
        private readonly VertexBuffer QuadVertexBuffer;
        private          IndexBuffer  RasterizeIndexBuffer;
        private          VertexBuffer RasterizeVertexBuffer;
        private          VertexBuffer RasterizeOffsetBuffer;

        private readonly AutoResetEvent UnlockedEvent = new AutoResetEvent(true);

        private static readonly short[] QuadIndices = new short[] {
            0, 1, 3, 1, 2, 3
        };

        internal long LastClearTimestamp;

        private int LastResetCount = 0;
        public event Action<ParticleSystem> OnDeviceReset;

        public ParticleSystem (
            ParticleEngine engine, ParticleSystemConfiguration configuration
        ) {
            Engine = engine;
            Configuration = configuration;
            LiveCount = 0;

            lock (engine.Coordinator.CreateResourceLock) {
                Slices = AllocateSlices();

                QuadIndexBuffer = new IndexBuffer(engine.Coordinator.Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
                QuadIndexBuffer.SetData(QuadIndices);

                const float argh = 102400;

                QuadVertexBuffer = new VertexBuffer(engine.Coordinator.Device, typeof(ParticleSystemVertex), 4, BufferUsage.WriteOnly);
                QuadVertexBuffer.SetData(new [] {
                    // HACK: Workaround for Intel's terrible video drivers.
                    // No, I don't know why.
                    new ParticleSystemVertex(-argh, -argh, 0),
                    new ParticleSystemVertex(argh, -argh, 1),
                    new ParticleSystemVertex(argh, argh, 2),
                    new ParticleSystemVertex(-argh, argh, 3)
                });

                FillIndexBuffer();
                FillVertexBuffer();
            }
        }

        public int Capacity {
            get {
                // FIXME
                return Slices[0].Count * Slice.Chunk.Width * Slice.Chunk.Height;
            }
        }

        private void FillIndexBuffer () {
            var buf = new short[] {
                0, 1, 3, 1, 2, 3
            };
            RasterizeIndexBuffer = new IndexBuffer(
                Engine.Coordinator.Device, IndexElementSize.SixteenBits, 
                buf.Length, BufferUsage.WriteOnly
            );
            RasterizeIndexBuffer.SetData(buf);
        }

        private void FillVertexBuffer () {
            {
                var buf = new ParticleSystemVertex[4];
                int i = 0;
                var v = new ParticleSystemVertex();
                buf[i++] = v;
                v.Corner = v.Unused = 1;
                buf[i++] = v;
                v.Corner = v.Unused = 2;
                buf[i++] = v;
                v.Corner = v.Unused = 3;
                buf[i++] = v;

                RasterizeVertexBuffer = new VertexBuffer(
                    Engine.Coordinator.Device, typeof(ParticleSystemVertex),
                    buf.Length, BufferUsage.WriteOnly
                );
                RasterizeVertexBuffer.SetData(buf);
            }

            {
                var buf = new ParticleOffsetVertex[Slice.Chunk.MaximumCount];

                for (var y = 0; y < Slice.Chunk.Height; y++) {
                    for (var x = 0; x < Slice.Chunk.Width; x++) {
                        var i = (y * Slice.Chunk.Width) + x;
                        buf[i].Offset = new Vector2(x / (float)Slice.Chunk.Width, y / (float)Slice.Chunk.Height);
                    }
                }

                RasterizeOffsetBuffer = new VertexBuffer(
                    Engine.Coordinator.Device, typeof(ParticleOffsetVertex),
                    buf.Length, BufferUsage.WriteOnly
                );
                RasterizeOffsetBuffer.SetData(buf);
            }
        }

        private void AllocateLivenessRT () {
            RenderTarget2D result;
            OcclusionQuery query;

            lock (Engine.Coordinator.CreateResourceLock) {
                /*
                result = new RenderTarget2D(
                    Engine.Coordinator.Device,
                    Slice.Chunk.Width / 8, Slice.Chunk.Height, false,                    
                    SurfaceFormat.Alpha8, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );
                */
                query = new OcclusionQuery(Engine.Coordinator.Device);
            }

            LivenessInfos.Add(new LivenessInfo {
                // Buffer = result,
                // Flags = new byte[result.Width * result.Height],
                Query = query,
                Count = 0
            });
        }

        private Slice[] AllocateSlices () {
            var result = new Slice[SliceCount];
            for (var i = 0; i < result.Length; i++)
                result[i] = new Slice(Engine.Coordinator.Device, i, Configuration.AttributeCount);

            return result;
        }

        private Slice GrabWriteSlice () {
            Slice dest = null;

            lock (Slices) {
                for (int i = 0; i < 10; i++) {
                    dest = (
                        from s in Slices where (!s.IsBeingGenerated && s.InUseCount <= 0)
                        orderby s.Timestamp select s
                    ).FirstOrDefault();

                    if (dest == null) {
                        // Console.WriteLine("Retry lock");
                        UnlockedEvent.WaitOne(2);
                    } else
                        break;
                }

                if (dest == null)
                    throw new Exception("Failed to lock any slices for write");

                dest.Lock("write");
                lock (dest) {
                    dest.IsValid = false;
                    dest.IsBeingGenerated = true;
                    dest.Timestamp = Time.Ticks;
                }
            }

            return dest;
        }

        /*
        private void ComputeLiveness (
            IBatchContainer container, int layer, Slice source
        ) {
            var e = Engine.ParticleMaterials.ComputeLiveness;
            var p = e.Parameters;

            using (var batch = BatchGroup.New(
                container, layer,
                after: (dm, _) => {
                    // Incredibly pointless cleanup mandated by XNA's bugs
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                for (int i = 0, l = source.Chunks.Count; i < l; i++) {
                    ComputeLivenessForChunk(
                        batch, i, source.Chunks[i], LivenessInfos[i]
                    );
                }
            }
        }

        private void ComputeLivenessForChunk (
            IBatchContainer container, int layer, Slice.Chunk chunk, LivenessInfo li
        ) {
            var m = Engine.ParticleMaterials.ComputeLiveness;
            var e = m.Effect;
            var p = e.Parameters;

            using (var batch = NativeBatch.New(
                container, layer, m,
                (dm, _) => {
                    dm.PushRenderTarget(li.Buffer);
                    dm.Device.Viewport = new Viewport(0, 0, Slice.Chunk.Width / 8, Slice.Chunk.Height);
                    dm.Device.Clear(Color.Transparent);
                    p["Texel"].SetValue(new Vector2(1f / Slice.Chunk.Width, 1f / Slice.Chunk.Height));
                    p["PositionTexture"].SetValue(chunk.PositionAndBirthTime);
                    m.Flush();
                },
                (dm, _) => {
                    // XNA effectparameter gets confused about whether a value is set or not, so we do this
                    //  to ensure it always re-sets the texture parameter
                    p["PositionTexture"].SetValue((Texture2D)null);
                    dm.PopRenderTarget();
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, QuadVertexBuffer, 0,
                    QuadIndexBuffer, 0, 0, QuadVertexBuffer.VertexCount, 0, QuadVertexBuffer.VertexCount / 2
                ));
            }
        }
        */

        private Slice.Chunk CreateChunk (GraphicsDevice device) {
            lock (Engine.Coordinator.CreateResourceLock)
                return new Slice.Chunk(Configuration.AttributeCount, device);
        }

        // Make sure to lock the slice first.
        public int InitializeNewChunks<TAttribute> (
            int particleCount,
            GraphicsDevice device,
            bool parallel,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            Action<TAttribute[], int> attributeInitializer
        ) where TAttribute : struct {
            var mc = Slice.Chunk.MaximumCount;
            int numToSpawn = (int)Math.Ceiling((double)particleCount / mc);

            if (parallel) {
                Parallel.For(
                    0, numToSpawn,
                    (i) => {
                        var c = CreateChunk(device);
                        c.Initialize(i * mc, positionInitializer, velocityInitializer, attributeInitializer);
                        lock (NewChunks)
                            NewChunks.Add(c);
                    }
                );
            } else {
                for (int i = 0; i < numToSpawn; i++) {
                    var c = CreateChunk(device);
                    c.Initialize(i * mc, positionInitializer, velocityInitializer, attributeInitializer);
                    lock (NewChunks)
                        NewChunks.Add(c);
                }
            }

            return numToSpawn * mc;
        }

        private void UpdatePass (
            IBatchContainer container, int layer, Material m,
            Slice source, Slice a, Slice b,
            ref Slice passSource, ref Slice passDest, 
            bool runOcclusionQuery, long startedWhen,
            Action<EffectParameterCollection> setParameters
        ) {
            var _source = passSource;
            var _dest = passDest;
            var device = container.RenderManager.DeviceManager.Device;

            while (_dest.Count < _source.Count) {
                lock (container.RenderManager.CreateResourceLock) {
                    var newChunk = CreateChunk(device);
                    _dest.Add(newChunk);
                }
            }

            lock (LivenessInfos)
            while (LivenessInfos.Count < source.Count)
                AllocateLivenessRT();

            var e = m.Effect;
            var p = e.Parameters;

            using (var batch = BatchGroup.New(
                container, layer,
                after: (dm, _) => {
                    // Incredibly pointless cleanup mandated by XNA's bugs
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                for (int i = 0, l = _source.Count; i < l; i++) {
                    var sourceChunk = _source[i];
                    var destChunk = _dest[i];

                    var li = LivenessInfos[i];
                    if (runOcclusionQuery)
                        li.LastQueryStart = Time.Ticks;

                    ChunkUpdatePass(
                        batch, i,
                        m, sourceChunk, destChunk,
                        setParameters, 
                        runOcclusionQuery ? li.Query : null
                    );
                }
            }

            if (_source == source) {
                if (_dest == a)
                    passDest = b;
                else if (_dest == b)
                    passDest = a;
                else
                    throw new Exception();

                passSource = _dest;
            } else {
                passDest = _source;
                passSource = _dest;
            }
        }

        private void ChunkUpdatePass (
            IBatchContainer container, int layer, Material m,
            Slice.Chunk source, Slice.Chunk dest,
            Action<EffectParameterCollection> setParameters,
            OcclusionQuery query
        ) {
            // Console.WriteLine("{0} -> {1}", passSource.Index, passDest.Index);
            var e = m.Effect;
            var p = e.Parameters;
            using (var batch = NativeBatch.New(
                container, layer, m,
                (dm, _) => {
                    dm.PushRenderTargets(dest.Bindings);
                    dm.Device.Viewport = new Viewport(0, 0, Slice.Chunk.Width, Slice.Chunk.Height);
                    dm.Device.Clear(Color.Transparent);
                    p["Texel"].SetValue(new Vector2(1f / Slice.Chunk.Width, 1f / Slice.Chunk.Height));

                    p["PositionTexture"].SetValue(source.PositionAndBirthTime);
                    p["VelocityTexture"].SetValue(source.Velocity);

                    var at = p["AttributeTexture"];
                    if (at != null)
                        at.SetValue(source.Attributes);

                    var dft = p["DistanceFieldTexture"];
                    if (dft != null)
                        dft.SetValue(Configuration.DistanceField.Texture);

                    if (setParameters != null)
                        setParameters(p);

                    m.Flush();

                    if (query != null) {
                        Monitor.Enter(query);
                        query.Begin();
                    }
                },
                (dm, _) => {
                    // XNA effectparameter gets confused about whether a value is set or not, so we do this
                    //  to ensure it always re-sets the texture parameter
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);

                    var at = p["AttributeTexture"];
                    if (at != null)
                        at.SetValue((Texture2D)null);

                    var dft = p["DistanceFieldTexture"];
                    if (dft != null)
                        dft.SetValue((Texture2D)null);

                    if (query != null) {
                        query.End();
                        Monitor.Exit(query);
                    }

                    dm.PopRenderTarget();
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, QuadVertexBuffer, 0,
                    QuadIndexBuffer, 0, 0, QuadVertexBuffer.VertexCount, 0, QuadVertexBuffer.VertexCount / 2
                ));
            }
        }

        public void Clear () {
            LastClearTimestamp = Time.Ticks;

            lock (LivenessInfos)
                foreach (var li in LivenessInfos)
                    li.Count = 0;
        }

        public int Spawn (
            int particleCount,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            bool parallel = true
        ) {
            return Spawn<float>(particleCount, positionInitializer, velocityInitializer, null, parallel);
        }

        public int Spawn<TAttribute> (
            int particleCount,
            Action<Vector4[], int> positionInitializer,
            Action<Vector4[], int> velocityInitializer,
            Action<TAttribute[], int> attributeInitializer,
            bool parallel = true
        ) where TAttribute : struct {
            Slice target;

            lock (Slices) {
                target = (
                    from s in Slices where (s.IsValid && s.Timestamp >= LastClearTimestamp)
                    orderby s.Timestamp descending select s
                ).FirstOrDefault() ?? Slices[0];
            }

            target.Lock("initialize");
            lock (target) {
                target.IsValid = false;
                target.IsBeingGenerated = true;
            }

            var result = InitializeNewChunks(
                particleCount,
                Engine.Coordinator.Device,
                parallel,
                positionInitializer,
                velocityInitializer,
                attributeInitializer
            );

            lock (target) {
                target.Timestamp = Time.Ticks;
                target.IsValid = true;
                target.IsBeingGenerated = false;
            }
            target.Unlock();

            return result;
        }

        private void UpdateLivenessAndReapDeadChunks () {
            lock (Engine.Coordinator.UseResourceLock) {
                var q = Engine.Coordinator.ThreadGroup.GetQueueForType<DownloadLiveness>();

                foreach (var li in LivenessInfos)
                    q.Enqueue(new DownloadLiveness(this, li));

                q.WaitUntilDrained();
            }

            LiveCount = (int)LivenessInfos.Sum(li => li.Count);

            for (int i = 0; i < LivenessInfos.Count; i++) {
                var li = LivenessInfos[i];
                if (li.Count > 0)
                    continue;

                lock (Slices) {
                    foreach (var s in Slices) {
                        var chunk = s.RemoveAt(i);
                        Engine.Coordinator.DisposeResource(chunk);
                    }

                    LivenessInfos.RemoveAt(i);
                }

                i -= 1;
            }
        }

        public void Update (IBatchContainer container, int layer) {
            var startedWhen = Time.Ticks;

            Slice source, a, b;
            Slice passSource, passDest;

            if (LastResetCount != Engine.ResetCount) {
                if (OnDeviceReset != null)
                    OnDeviceReset(this);
                LastResetCount = Engine.ResetCount;
            }

            lock (LivenessInfos)
                UpdateLivenessAndReapDeadChunks();

            lock (Slices) {
                source = (
                    from s in Slices where (s.IsValid && s.Timestamp >= LastClearTimestamp)
                    orderby s.Timestamp descending select s
                ).FirstOrDefault();

                if (source == null) {
                    // A clear occurred
                    return;
                }

                source.Lock("update");
            }

            lock (NewChunks) {
                foreach (var nc in NewChunks) {
                    nc.Index = source.Count;
                    source.Add(nc);
                }

                NewChunks.Clear();
            }

            a = GrabWriteSlice();
            b = GrabWriteSlice();
            passSource = source;
            passDest = a;

            var pm = Engine.ParticleMaterials;

            using (var group = BatchGroup.New(
                container, layer
            )) {
                int i = 0;
                bool occlusionQueryPending = true;

                foreach (var t in Transforms) {
                    if (!t.IsActive)
                        continue;

                    UpdatePass(
                        group, i++, t.GetMaterial(Engine.ParticleMaterials),
                        source, a, b, ref passSource, ref passDest, 
                        occlusionQueryPending, startedWhen,
                        t.SetParameters
                    );

                    occlusionQueryPending = false;
                }

                if (Configuration.DistanceField != null) {
                    UpdatePass(
                        group, i++, pm.UpdateWithDistanceField,
                        source, a, b, ref passSource, ref passDest,
                        occlusionQueryPending, startedWhen,
                        (p) => {
                            var dfu = new Uniforms.DistanceField(Configuration.DistanceField, Configuration.DistanceFieldMaximumZ);
                            pm.MaterialSet.TrySetBoundUniform(pm.UpdateWithDistanceField, "DistanceField", ref dfu);

                            p["MaximumEncodedDistance"].SetValue(Configuration.DistanceField.MaximumEncodedDistance);
                            p["EscapeVelocity"].SetValue(Configuration.EscapeVelocity);
                            p["BounceVelocityMultiplier"].SetValue(Configuration.BounceVelocityMultiplier);
                            p["LifeDecayRate"].SetValue(Configuration.GlobalLifeDecayRate);
                            p["MaximumVelocity"].SetValue(Configuration.MaximumVelocity);
                            p["CollisionDistance"].SetValue(Configuration.CollisionDistance);
                        }
                    );
                } else {
                    UpdatePass(
                        group, i++, pm.UpdatePositions,
                        source, a, b, ref passSource, ref passDest,
                        occlusionQueryPending, startedWhen,
                        (p) => {
                            p["LifeDecayRate"].SetValue(Configuration.GlobalLifeDecayRate);
                            p["MaximumVelocity"].SetValue(Configuration.MaximumVelocity);
                        }
                    );
                }

                // ComputeLiveness(group, i++, passSource);
            }

            var ts = Time.Ticks;

            // TODO: Do this immediately after issuing the batch instead?
            Engine.Coordinator.AfterPresent(() => {
                lock (passSource) {
                    // Console.WriteLine("Validate {0}", passSource.Index);
                    passSource.Timestamp = ts;
                    passSource.IsValid = true;
                    passSource.IsBeingGenerated = false;
                }

                a.IsBeingGenerated = false;
                b.IsBeingGenerated = false;

                source.Unlock();
                a.Unlock();
                b.Unlock();

                UnlockedEvent.Set();
            });
        }

        private void RenderChunk (
            BatchGroup group, Slice.Chunk chunk,
            Material m
        ) {
            // TODO: Actual occupied count?
            var quadCount = Slice.Chunk.MaximumCount;

            using (var batch = NativeBatch.New(
                group, chunk.Index, m, (dm, _) => {
                    var p = m.Effect.Parameters;
                    p["PositionTexture"].SetValue(chunk.PositionAndBirthTime);
                    p["VelocityTexture"].SetValue(chunk.Velocity);
                    p["AttributeTexture"].SetValue(chunk.Attributes);
                    m.Flush();
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, 
                    RasterizeVertexBuffer, 0,
                    RasterizeOffsetBuffer, 0, 
                    null, 0,
                    RasterizeIndexBuffer, 0, 0, 4, 0, 2,
                    quadCount
                ));
            }
        }

        public void Render (
            IBatchContainer container, int layer,
            Material material = null,
            Matrix? transform = null, 
            BlendState blendState = null
        ) {
            Slice source;

            var startedWhen = Time.Ticks;

            lock (Slices) {
                source = (
                    from s in Slices where (s.IsValid && s.Timestamp >= LastClearTimestamp)
                    orderby s.Timestamp descending select s
                ).FirstOrDefault();

                // A clear occurred
                if (source == null)
                    return;

                source.Lock("render");
            }

            var m = Engine.Materials.Get(
                material ?? Engine.ParticleMaterials.White, blendState: blendState
            );
            var e = m.Effect;
            var p = e.Parameters;
            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    // TODO: transform arg
                    p["BitmapTexture"].SetValue(Configuration.Texture);
                    p["BitmapTextureRegion"].SetValue(new Vector4(
                        Configuration.TextureRegion.TopLeft, 
                        Configuration.TextureRegion.BottomRight.X, 
                        Configuration.TextureRegion.BottomRight.Y
                    ));
                    p["AnimationRate"].SetValue(Configuration.AnimationRate);
                    p["Size"].SetValue(Configuration.Size / 2);
                    p["VelocityRotation"].SetValue(Configuration.RotationFromVelocity ? 1f : 0f);
                    p["OpacityFromLife"].SetValue(Configuration.OpacityFromLife);
                    p["Texel"].SetValue(new Vector2(1f / Slice.Chunk.Width, 1f / Slice.Chunk.Height));
                },
                (dm, _) => {
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);
                    p["AttributeTexture"].SetValue((Texture2D)null);
                    p["BitmapTexture"].SetValue((Texture2D)null);
                    // ughhhhhhhhhh
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                for (int i = 0, c = source.Count; i < c; i++) {
                    var chunk = source[i];
                    RenderChunk(group, chunk, m);
                }
            }

            // TODO: Do this immediately after issuing the batch instead?
            Engine.Coordinator.AfterPresent(() => {
                source.Unlock();
                UnlockedEvent.Set();
            });
        }

        public void Dispose () {
            foreach (var slice in Slices)
                Engine.Coordinator.DisposeResource(slice);
            foreach (var li in LivenessInfos)
                Engine.Coordinator.DisposeResource(li);
        }
    }

    public class ParticleSystemConfiguration {
        public readonly int AttributeCount;

        // Particles that reach this age are killed
        // Defaults to (effectively) not killing particles
        public int MaximumAge = 1024 * 1024 * 8;

        // Configures the sprite rendered for each particle
        public Texture2D Texture;
        public Bounds    TextureRegion = new Bounds(Vector2.Zero, Vector2.One);
        public Vector2   Size = Vector2.One;

        // Animates through the sprite texture based on the particle's life value, if set
        // Smaller values will result in slower animation. Zero turns off animation.
        public Vector2 AnimationRate;

        // If set, particles will rotate based on their direction of movement
        public bool RotationFromVelocity;

        // If != 0, a particle's opacity is equal to its life divided by this value
        public float OpacityFromLife = 0;

        // Life of all particles decreases by this much every update
        public float GlobalLifeDecayRate = 1;

        // If set, particles collide with volumes in this distance field
        public DistanceField DistanceField;
        public float         DistanceFieldMaximumZ;

        // The distance at which a particle is considered colliding with the field.
        // Raise this to make particles 'larger'.
        public float         CollisionDistance = 0.5f;

        // Particles will not be allowed to exceed this velocity
        public float         MaximumVelocity = 9999f;

        // Particles trapped inside distance field volumes will attempt to escape
        //  at this velocity multiplied by their distance from the outside
        public float         EscapeVelocity = 1.0f;
        // Particles colliding with distance field volumes will retain this much
        //  of their speed and bounce off of the volume
        public float         BounceVelocityMultiplier = 0.0f;

        public ParticleSystemConfiguration (
            int attributeCount = 0
        ) {
            AttributeCount = attributeCount;
        }
    }

    internal struct DownloadLiveness : Threading.IWorkItem {
        public ParticleSystem System;
        public ParticleSystem.LivenessInfo Target;

        public DownloadLiveness (ParticleSystem system, ParticleSystem.LivenessInfo target) {
            System = system;
            Target = target;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint popcnt (uint i) {
             i = i - ((i >> 1) & 0x55555555);
             i = (i & 0x33333333) + ((i >> 2) & 0x33333333);
             return (((i + (i >> 4)) & 0x0F0F0F0F) * 0x01010101) >> 24;
        }

        public void Execute () {
            /*
            Target.Buffer.GetData(Target.Flags);

            uint newCount = 0;
            foreach (byte b in Target.Flags)
                newCount += popcnt(b);

            Target.Count = newCount;
            */

            var waitStarted = Time.Ticks;
            lock (Target) {
                while ((Target.LastQueryStart > System.LastClearTimestamp) && !Target.Query.IsComplete)
                    Thread.Sleep(0);
                var waitEnded = Time.Ticks;
                var elapsedMs = ((waitEnded - waitStarted) * 100 / Time.MillisecondInTicks) / 100.0;

                if (Target.LastQueryStart <= System.LastClearTimestamp)
                    Target.Count = 0;
                else
                    Target.Count = (uint)Target.Query.PixelCount;
            }
        }
    }
}