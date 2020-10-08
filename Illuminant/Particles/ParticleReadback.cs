using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Squared.Game;
using Squared.Render;
using Squared.Render.Evil;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant.Particles {
    public partial class ParticleSystem : IParticleSystems {
        private object ReadbackLock = new object();
        private float  ReadbackTimestamp;
        private Future<ArraySegment<BitmapDrawCall>> ReadbackFuture = new Future<ArraySegment<BitmapDrawCall>>();
        private BitmapDrawCall[] ReadbackResultBuffer;
        private Vector4[] ReadbackBuffer1, ReadbackBuffer2, ReadbackBuffer3;

        private void MaybePerformReadback (float timestamp) {
            Future<ArraySegment<BitmapDrawCall>> f;

            lock (ReadbackLock) {
                if (!Configuration.AutoReadback)
                    return;

                f = ReadbackFuture;
                if (f == null)
                    return;
            }

            ReadbackTimestamp = timestamp;
            var chunkCount = Engine.Configuration.ChunkSize * Engine.Configuration.ChunkSize;
            int maxTotalCount;
            lock (Chunks)
                maxTotalCount = Chunks.Count * chunkCount;
            var bufferSize = (int)Math.Ceiling(maxTotalCount / 4096.0) * 4096;
            AutoGrowBuffer(ref ReadbackBuffer1, chunkCount);
            AutoGrowBuffer(ref ReadbackBuffer2, chunkCount);
            AutoGrowBuffer(ref ReadbackBuffer3, chunkCount);
            AutoGrowBuffer(ref ReadbackResultBuffer, maxTotalCount);

            // FIXME: This is too slow
            // Array.Clear(ReadbackResultBuffer, 0, ReadbackResultBuffer.Length);

            Configuration.Appearance?.Texture?.EnsureInitialized(Engine.Configuration.TextureLoader);

            int totalCount = 0;
            // FIXME: Do this in parallel
            lock (Chunks)
            foreach (var c in Chunks) {
                var curr = c.Current;
                if (curr.IsDisposed)
                    continue;
                var rowCount = (int)Math.Ceiling(c.TotalSpawned / (float)Engine.Configuration.ChunkSize);
                var eleCount = rowCount * Engine.Configuration.ChunkSize;
                var rect = new Rectangle(0, 0, Engine.Configuration.ChunkSize, rowCount);
                curr.PositionAndLife.GetDataFast(0, rect, ReadbackBuffer1, 0, eleCount);
                c.RenderData.GetDataFast(0, rect, ReadbackBuffer2, 0, eleCount);
                c.RenderColor.GetDataFast(0, rect, ReadbackBuffer3, 0, eleCount);
                totalCount += FillReadbackResult(
                    ReadbackResultBuffer, ReadbackBuffer1, ReadbackBuffer2, ReadbackBuffer3,
                    totalCount, eleCount, ReadbackTimestamp
                );
            }

            f.SetResult(new ArraySegment<BitmapDrawCall>(
                ReadbackResultBuffer, 0, totalCount
            ), null);
        }

        private int FillReadbackResult (
            BitmapDrawCall[] buffer, Vector4[] positionAndLife, Vector4[] renderData, Vector4[] renderColor,
            int offset, int count, float now
        ) {
            // var sfl = new ClampedBezier1(Configuration.SizeFromLife);

            Vector2 pSize;
            BitmapDrawCall dc = default(BitmapDrawCall);
            dc.Texture = Configuration.Appearance?.Texture?.Instance;
            dc.Origin = Vector2.One * 0.5f;

            var animRate = Configuration.Appearance?.AnimationRate ?? Vector2.Zero;
            var animRateAbs = new Vector2(Math.Abs(animRate.X), Math.Abs(animRate.Y));
            var cfv = Configuration.Appearance?.ColumnFromVelocity ?? false;
            var rfv = Configuration.Appearance?.RowFromVelocity ?? false;
            var c = Color.White;

            var region = Bounds.Unit;

            if (dc.Texture != null) {
                var sizeF = new Vector2(dc.Texture.Width, dc.Texture.Height);
                dc.TextureRegion = region = Bounds.FromPositionAndSize(
                    Configuration.Appearance.OffsetPx / sizeF,
                    Configuration.Appearance.SizePx.GetValueOrDefault(sizeF) / sizeF
                );
                if (Configuration.Appearance.RelativeSize)
                    pSize = Configuration.Size;
                else
                    pSize = Configuration.Size / sizeF;
            } else {
                pSize = Configuration.Size;
            }

            var texSize = region.Size;
            var frameCountX = Math.Max((int)(1.0f / texSize.X), 1);
            var frameCountY = Math.Max((int)(1.0f / texSize.Y), 1);
            var maxAngleX = (2 * Math.PI) / frameCountX;
            var maxAngleY = (2 * Math.PI) / frameCountY;
            var velRotation = Configuration.RotationFromVelocity ? 1.0 : 0.0f;

            var sr = Configuration.SortedReadback;
            var zToY = Configuration.ZToY;

            int result = 0;
            for (int i = 0, l = count; i < l; i++) {
                var pAndL = positionAndLife[i];
                var life = pAndL.W;
                if (life <= 0)
                    continue;

                var rd = renderData[i];
                var rc = renderColor[i];

                var sz = rd.X;
                var rot = rd.Y % (float)(2 * Math.PI);

                if ((frameCountX > 1) || (frameCountY > 1)) {
                    var frameIndexXy = (animRateAbs * life).Floor();

                    frameIndexXy.Y += (float)Math.Floor(rd.W);
                    if (cfv)
                        frameIndexXy.X += (float)Math.Round(rot / maxAngleX);
                    if (rfv)
                        frameIndexXy.Y += (float)Math.Round(rot / maxAngleY);

                    frameIndexXy.X = Math.Max(0, frameIndexXy.X) % frameCountX;
                    frameIndexXy.Y = Arithmetic.Clamp(frameIndexXy.Y, 0, frameCountY - 1);
                    if (animRate.X < 0)
                        frameIndexXy.X = frameCountX - frameIndexXy.X;
                    if (animRate.Y < 0)
                        frameIndexXy.Y = frameCountY - frameIndexXy.Y;
                    var texOffset = frameIndexXy * texSize;

                    dc.TextureRegion = region;
                    dc.TextureRegion.TopLeft += texOffset;
                    dc.TextureRegion.BottomRight += texOffset;
                }

                dc.Position = new Vector2(pAndL.X, pAndL.Y);
                if (sr)
                    dc.SortOrder = pAndL.Y + zToY;
                dc.Scale = pSize * sz;
                c.R = (byte)(rc.X * 255);
                c.G = (byte)(rc.Y * 255);
                c.B = (byte)(rc.Z * 255);
                c.A = (byte)(rc.W * 255);
                dc.MultiplyColor = c;
                dc.Rotation = (float)(velRotation * rot);

                buffer[result + offset] = dc;
                result++;
            }

            return result;
        }
    }
}
