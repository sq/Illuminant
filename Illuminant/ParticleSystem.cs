using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;

namespace Squared.Illuminant {
    public class ParticleSystem : IDisposable {
        private class Slice : IDisposable {
            public readonly int Index;
            public readonly int ColumnCount, RowCount;
            public long Timestamp;
            public bool IsValid, IsBeingGenerated;
            public int  InUseCount;
            public RenderTarget2D PositionAndBirthTime;
            public RenderTarget2D Velocity;
            public RenderTarget2D Attributes;

            public Slice (
                GraphicsDevice device, int index, int columnCount, int rowCount,
                int attributeCount
            ) {
                Index = index;
                ColumnCount = columnCount;
                RowCount = rowCount;
                PositionAndBirthTime = new RenderTarget2D(
                    device,
                    columnCount, rowCount, false,
                    SurfaceFormat.Vector4, DepthFormat.None,
                    0, RenderTargetUsage.PreserveContents
                );
                Velocity = new RenderTarget2D(
                    device,
                    columnCount, rowCount, false,
                    SurfaceFormat.Vector4, DepthFormat.None,
                    0, RenderTargetUsage.PreserveContents
                );

                // FIXME
                if (attributeCount == 1)
                    Attributes = new RenderTarget2D(
                        device,
                        columnCount, rowCount, false,
                        SurfaceFormat.Vector4, DepthFormat.None,
                        0, RenderTargetUsage.PreserveContents
                    );
                else if (attributeCount != 0)
                    throw new ArgumentException("Only one attribute is supported");

                Timestamp = Squared.Util.Time.Ticks;
            }

            // Make sure to lock the slice first.
            public void Initialize<TAttribute> (
                Action<Vector4[]> positionInitializer,
                Action<Vector4[]> velocityInitializer,
                Action<TAttribute[]> attributeInitializer
            ) where TAttribute : struct {
                var buf = new Vector4[ColumnCount * RowCount];

                if (positionInitializer != null) {
                    positionInitializer(buf);
                    PositionAndBirthTime.SetData(buf);
                }

                if (velocityInitializer != null) {
                    velocityInitializer(buf);
                    Velocity.SetData(buf);
                }

                if ((attributeInitializer != null) && (Attributes != null)) {
                    var abuf = new TAttribute[ColumnCount * RowCount];
                    attributeInitializer(abuf);
                    Attributes.SetData(abuf);
                }

                IsValid = true;
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
                PositionAndBirthTime.Dispose();
                Velocity.Dispose();
            }
        }

        public readonly int RowCount;
        public          int LiveCount { get; private set; }

        public readonly ParticleEngine                     Engine;
        public readonly ParticleSystemConfiguration        Configuration;
        public readonly List<Transforms.ParticleTransform> Transforms = 
            new List<Illuminant.Transforms.ParticleTransform>();

        // 3 because we go
        // old -> a -> b -> a -> ... -> done
        private const int SliceCount          = 3;
        private const int RasterChunkRowCount = 16;
        private readonly int[] DeadCountPerRow;

        private Slice[] Slices;
        private RenderTarget2D 
            // Used to locate empty slots to spawn new particles into
            // We generate this every frame from a pass over the position buffer
            ParticleAgeFractionBuffer,
            // We generate this every frame from a pass over the age fraction buffer
            LiveParticleCountBuffer;

        private readonly IndexBuffer  QuadIndexBuffer;
        private readonly VertexBuffer QuadVertexBuffer;
        private          IndexBuffer  RasterizeIndexBuffer;
        private          VertexBuffer RasterizeVertexBuffer;
        private          VertexBuffer RasterizeOffsetBuffer;

        private readonly AutoResetEvent UnlockedEvent = new AutoResetEvent(true);

        private static readonly short[] QuadIndices = new short[] {
            0, 1, 3, 1, 2, 3
        };

        private int LastResetCount = 0;
        public event Action<ParticleSystem> OnDeviceReset;

        public ParticleSystem (
            ParticleEngine engine, ParticleSystemConfiguration configuration
        ) {
            Engine = engine;
            Configuration = configuration;
            RowCount = (Configuration.MaximumCount + Configuration.ParticlesPerRow - 1) / Configuration.ParticlesPerRow;
            DeadCountPerRow = new int[RowCount];
            LiveCount = 0;

            lock (engine.Coordinator.CreateResourceLock) {
                Slices = AllocateSlices();

                // TODO: Bitpack?
                ParticleAgeFractionBuffer = new RenderTarget2D(
                    engine.Coordinator.Device,
                    Configuration.ParticlesPerRow, RowCount, false,                    
                    SurfaceFormat.Alpha8, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );

                LiveParticleCountBuffer = new RenderTarget2D(
                    engine.Coordinator.Device,
                    RowCount, 1, false,
                    SurfaceFormat.Single, DepthFormat.None, 
                    0, RenderTargetUsage.PreserveContents
                );

                QuadIndexBuffer = new IndexBuffer(engine.Coordinator.Device, IndexElementSize.SixteenBits, 6, BufferUsage.WriteOnly);
                QuadIndexBuffer.SetData(QuadIndices);

                QuadVertexBuffer = new VertexBuffer(engine.Coordinator.Device, typeof(ParticleSystemVertex), 4, BufferUsage.WriteOnly);
                QuadVertexBuffer.SetData(new [] {
                    new ParticleSystemVertex(0, 0, 0),
                    new ParticleSystemVertex(1, 0, 1),
                    new ParticleSystemVertex(1, 1, 2),
                    new ParticleSystemVertex(0, 1, 3)
                });

                FillIndexBuffer();
                FillVertexBuffer();
            }

            Initialize(null, null);
        }

        private void FillIndexBuffer () {
            var buf = new short[Configuration.ParticlesPerRow * 6];
            int i = 0, j = 0;
            while (i < buf.Length) {
                buf[i++] = (short)(j + 0);
                buf[i++] = (short)(j + 1);
                buf[i++] = (short)(j + 3);
                buf[i++] = (short)(j + 1);
                buf[i++] = (short)(j + 2);
                buf[i++] = (short)(j + 3);

                j += 4;
            }

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
                var buf = new ParticleOffsetVertex[RasterChunkRowCount * Configuration.ParticlesPerRow];

                for (var y = 0; y < RasterChunkRowCount; y++) {
                    for (var x = 0; x < Configuration.ParticlesPerRow; x++) {
                        var i = (y * Configuration.ParticlesPerRow) + x;
                        buf[i].Offset = new Vector2(x / (float)Configuration.ParticlesPerRow, y / (float)RowCount);
                    }
                }

                RasterizeOffsetBuffer = new VertexBuffer(
                    Engine.Coordinator.Device, typeof(ParticleOffsetVertex),
                    buf.Length, BufferUsage.WriteOnly
                );
                RasterizeOffsetBuffer.SetData(buf);
            }
        }

        private Slice[] AllocateSlices () {
            var result = new Slice[SliceCount];
            for (var i = 0; i < result.Length; i++)
                result[i] = new Slice(
                    Engine.Coordinator.Device, i, Configuration.ParticlesPerRow, 
                    RowCount, Configuration.AttributeCount
                );

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
                    dest.Timestamp = Squared.Util.Time.Ticks;
                }
            }

            return dest;
        }

        private void UpdatePass (
            IBatchContainer container, int layer, Material m,
            Slice source, Slice a, Slice b,
            ref Slice passSource, ref Slice passDest, Action<EffectParameterCollection> setParameters
        ) {
            // Console.WriteLine("{0} -> {1}", passSource.Index, passDest.Index);

            var _source = passSource;
            var _dest = passDest;

            RenderTargetBinding[] bindings;            
            if (_dest.Attributes != null)
                bindings = new[] {
                    new RenderTargetBinding(_dest.PositionAndBirthTime),
                    new RenderTargetBinding(_dest.Velocity),
                    new RenderTargetBinding(_dest.Attributes)
                };
            else
                bindings = new[] {
                    new RenderTargetBinding(_dest.PositionAndBirthTime),
                    new RenderTargetBinding(_dest.Velocity)
                };

            var e = m.Effect;
            using (var batch = NativeBatch.New(
                container, layer, m,
                (dm, _) => {
                    dm.PushRenderTargets(bindings);
                    dm.Device.Viewport = new Viewport(0, 0, Configuration.ParticlesPerRow, RowCount);
                    dm.Device.Clear(Color.Transparent);
                    var p = e.Parameters;
                    p["PositionTexture"].SetValue(_source.PositionAndBirthTime);
                    p["VelocityTexture"].SetValue(_source.Velocity);
                    p["AttributeTexture"].SetValue(_source.Attributes);
                    p["HalfTexel"].SetValue(new Vector2(0.5f / Configuration.ParticlesPerRow, 0.5f / RowCount));
                    if (setParameters != null)
                        setParameters(p);
                    m.Flush();
                },
                (dm, _) => {
                    dm.PopRenderTarget();
                    var p = e.Parameters;
                    p["PositionTexture"].SetValue((Texture2D)null);
                    p["VelocityTexture"].SetValue((Texture2D)null);
                    p["AttributeTexture"].SetValue((Texture2D)null);
                    // fuck offfff
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                batch.Add(new NativeDrawCall(
                    PrimitiveType.TriangleList, QuadVertexBuffer, 0,
                    QuadIndexBuffer, 0, 0, QuadVertexBuffer.VertexCount, 0, QuadVertexBuffer.VertexCount / 2
                ));
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

        public void Initialize (
            Action<Vector4[]> positionInitializer,
            Action<Vector4[]> velocityInitializer
        ) {
            Initialize<float>(positionInitializer, velocityInitializer, null);
        }

        public void Initialize<TAttribute> (
            Action<Vector4[]> positionInitializer,
            Action<Vector4[]> velocityInitializer,
            Action<TAttribute[]> attributeInitializer
        ) where TAttribute : struct {
            Slice target;

            lock (Slices) {
                target = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).FirstOrDefault() ?? Slices[0];
            }

            target.Lock("initialize");
            lock (target) {
                target.IsValid = false;
                target.IsBeingGenerated = true;
            }

            foreach (var s in Slices) {
                lock (s)
                    s.IsValid = false;
            }

            target.Initialize(
                positionInitializer,
                velocityInitializer,
                attributeInitializer
            );

            lock (target) {
                target.Timestamp = Squared.Util.Time.Ticks;
                target.IsValid = true;
                target.IsBeingGenerated = false;
            }
            target.Unlock();
        }

        public void Update (IBatchContainer container, int layer) {
            Slice source, a, b;
            Slice passSource, passDest;

            if (LastResetCount != Engine.ResetCount) {
                if (OnDeviceReset != null)
                    OnDeviceReset(this);
                LastResetCount = Engine.ResetCount;
            }

            lock (Slices) {
                source = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).First();

                source.Lock("update");
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
                foreach (var t in Transforms) {
                    if (!t.IsActive)
                        continue;

                    UpdatePass(
                        group, i++, t.GetMaterial(Engine.ParticleMaterials),
                        source, a, b, ref passSource, ref passDest,
                        t.SetParameters
                    );
                }

                if (Configuration.DistanceField != null) {
                    UpdatePass(
                        group, i++, pm.UpdateWithDistanceField,
                        source, a, b, ref passSource, ref passDest,
                        (p) => {
                            var dfu = new Uniforms.DistanceField(Configuration.DistanceField, Configuration.DistanceFieldMaximumZ);
                            pm.MaterialSet.TrySetBoundUniform(pm.UpdateWithDistanceField, "DistanceField", ref dfu);

                            p["MaximumEncodedDistance"].SetValue(Configuration.DistanceField.MaximumEncodedDistance);
                            p["DistanceFieldTexture"].SetValue(Configuration.DistanceField.Texture);
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
                        (p) => {
                            p["LifeDecayRate"].SetValue(Configuration.GlobalLifeDecayRate);
                            p["MaximumVelocity"].SetValue(Configuration.MaximumVelocity);
                        }
                    );
                }
            }

            // TODO: Do this immediately after issuing the batch instead?
            Engine.Coordinator.AfterPresent(() => {
                lock (passSource) {
                    // Console.WriteLine("Validate {0}", passSource.Index);
                    passSource.Timestamp = Squared.Util.Time.Ticks;
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

        public void Render (
            IBatchContainer container, int layer,
            Material material = null,
            Matrix? transform = null, 
            BlendState blendState = null
        ) {
            Slice source;

            lock (Slices) {
                source = (
                    from s in Slices where s.IsValid
                    orderby s.Timestamp descending select s
                ).First();

                source.Lock("render");
            }

            var m = Engine.Materials.Get(
                material ?? Engine.ParticleMaterials.White, blendState: blendState
            );
            var e = m.Effect;
            using (var group = BatchGroup.New(
                container, layer,
                (dm, _) => {
                    // TODO: transform arg
                    e.Parameters["PositionTexture"].SetValue(source.PositionAndBirthTime);
                    e.Parameters["VelocityTexture"].SetValue(source.Velocity);
                    e.Parameters["AttributeTexture"].SetValue(source.Attributes);
                    e.Parameters["BitmapTexture"].SetValue(Configuration.Texture);
                    e.Parameters["BitmapTextureRegion"].SetValue(new Vector4(
                        Configuration.TextureRegion.TopLeft, 
                        Configuration.TextureRegion.BottomRight.X, 
                        Configuration.TextureRegion.BottomRight.Y
                    ));
                    e.Parameters["AnimationRate"].SetValue(Configuration.AnimationRate);
                    e.Parameters["Size"].SetValue(Configuration.Size / 2);
                    e.Parameters["VelocityRotation"].SetValue(Configuration.RotationFromVelocity ? 1f : 0f);
                    e.Parameters["OpacityFromLife"].SetValue(Configuration.OpacityFromLife);
                    e.Parameters["HalfTexel"].SetValue(new Vector2(0.5f / Configuration.ParticlesPerRow, 0.5f / RowCount));
                    m.Flush();
                },
                (dm, _) => {
                    e.Parameters["PositionTexture"].SetValue((Texture2D)null);
                    e.Parameters["VelocityTexture"].SetValue((Texture2D)null);
                    e.Parameters["AttributeTexture"].SetValue((Texture2D)null);
                    e.Parameters["BitmapTexture"].SetValue((Texture2D)null);
                    // ughhhhhhhhhh
                    for (var i = 0; i < 4; i++)
                        dm.Device.VertexTextures[i] = null;
                    for (var i = 0; i < 16; i++)
                        dm.Device.Textures[i] = null;
                }
            )) {
                int chunkCount = (RowCount + RasterChunkRowCount - 1) / RasterChunkRowCount;
                for (var i = 0; i < chunkCount; i++) {
                    var rowIndex = (i * RasterChunkRowCount);
                    var rowsToRender = Math.Min(RowCount - rowIndex, RasterChunkRowCount);
                    var quadCount = rowsToRender * Configuration.ParticlesPerRow;
                    var offset = new Vector2(0, rowIndex / (float)RowCount);
                    using (var chunk = NativeBatch.New(
                        group, i, m, (dm, _) => {
                            e.Parameters["SourceCoordinateOffset"].SetValue(offset);
                            m.Flush();
                        }
                    )) {
                        chunk.Add(new NativeDrawCall(
                            PrimitiveType.TriangleList, 
                            RasterizeVertexBuffer, 0,
                            RasterizeOffsetBuffer, 0, 
                            null, 0,
                            RasterizeIndexBuffer, 0, 0, 4, 0, 2,
                            quadCount
                        ));
                    }
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

            Engine.Coordinator.DisposeResource(ParticleAgeFractionBuffer);
            Engine.Coordinator.DisposeResource(LiveParticleCountBuffer);
        }
    }

    public class ParticleSystemConfiguration {
        public readonly int MaximumCount;
        public readonly int ParticlesPerRow;
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
            int attributeCount = 0,
            int maximumCount = 4096,
            int particlesPerRow = 64
        ) {
            AttributeCount = attributeCount;
            MaximumCount = maximumCount;
            ParticlesPerRow = particlesPerRow;
        }
    }
}
