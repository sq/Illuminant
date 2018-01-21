using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using Squared.Render;
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant {
    public struct HDRConfiguration {
        public struct GammaCompressionConfiguration {
            public float MiddleGray, AverageLuminance, MaximumLuminance;
        }

        public struct ToneMappingConfiguration {
            public float WhitePoint;
        }

        public HDRMode Mode;
        public float InverseScaleFactor;
        public float Offset;
        public GammaCompressionConfiguration GammaCompression;
        public ToneMappingConfiguration ToneMapping;

        private float ExposureMinusOne;

        public float Exposure {
            get {
                return ExposureMinusOne + 1;
            }
            set {
                ExposureMinusOne = value - 1;
            }
        }
    }

    public enum HDRMode {
        None,
        GammaCompress,
        ToneMap
    }    

    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private struct HistogramUpdateTask : IWorkItem {
            public RenderTarget2D Texture;
            public int LevelIndex;
            public Histogram Histogram;
            public float[] Buffer;
            public float ScaleFactor;
            public int Width, Height;
            public Action<Histogram> OnComplete;

            public void Execute () {
                var count = Width * Height;

                Texture.GetData(
                    LevelIndex, new Rectangle(0, 0, Width, Height),
                    Buffer, 0, count
                );

                lock (Histogram) {
                    Histogram.Clear();
                    Histogram.Add(Buffer, count, ScaleFactor);
                }

                if (OnComplete != null)
                    OnComplete(Histogram);
            }
        }

        private float ComputePercentile (float percentage, float[] buffer, int lastZero, int count, float effectiveScaleFactor) {
            count -= lastZero;
            var index = (int)(count * percentage / 100f);
            if (index < 0)
                index = 0;
            if (index >= count)
                index = count - 1;

            return buffer[lastZero + index];
        }

        public struct RenderedLighting {
            public  readonly LightingRenderer Renderer;
            public  readonly float            InverseScaleFactor;
            private readonly RenderTarget2D   Lightmap;
            internal         RenderTarget2D   LuminanceBuffer;
            private readonly int              Width, Height;

            internal RenderedLighting (
                LightingRenderer renderer, RenderTarget2D lightmap, float inverseScaleFactor
            ) {
                Renderer = renderer;
                Lightmap = lightmap;
                LuminanceBuffer = null;
                InverseScaleFactor = inverseScaleFactor;
                Width = renderer.Configuration.RenderSize.First;
                Height = renderer.Configuration.RenderSize.Second;
            }

            public bool IsValid {
                get {
                    return (Renderer != null) && (Lightmap != null);
                }
            }

            public void Resolve (
                IBatchContainer container, int layer,
                float? width = null, float? height = null,
                HDRConfiguration? hdr = null
            ) {
                if (!IsValid)
                    throw new InvalidOperationException("Invalid");

                Renderer.ResolveLighting(
                    container, layer,
                    Lightmap, width, height, hdr
                );
            }

            /// <param name="accuracyFactor">Governs how many pixels will be analyzed. Higher values are lower accuracy (but faster).</param>
            public bool TryComputeHistogram (
                Histogram histogram,
                Action<Histogram> onComplete,
                int accuracyFactor = 3
            ) {
                if (Renderer == null)
                    return false;
                if (LuminanceBuffer == null)
                    return false;

                var levelIndex = Math.Min(accuracyFactor, LuminanceBuffer.LevelCount - 1);
                var divisor = (int)Math.Pow(2, levelIndex);
                var levelWidth = LuminanceBuffer.Width / divisor;
                var levelHeight = LuminanceBuffer.Height / divisor;
                var count = levelWidth * levelHeight;

                float[] buffer;

                lock (Renderer._LuminanceReadbackArrayLock) {
                    buffer = Renderer._LuminanceReadbackArray;
                    if ((buffer == null) || (buffer.Length < count))
                        buffer = Renderer._LuminanceReadbackArray = new float[count];
                }

                var q = Renderer.Coordinator.ThreadGroup.GetQueueForType<HistogramUpdateTask>();

                var self = this;

                Renderer.Coordinator.AfterPresent(() => {
                    q.WaitUntilDrained();

                    q.Enqueue(new HistogramUpdateTask {
                        Texture = self.LuminanceBuffer,
                        LevelIndex = levelIndex,
                        Histogram = histogram,
                        Buffer = buffer,
                        Width = self.Width / 2 / divisor,
                        Height = self.Height / 2 / divisor,
                        ScaleFactor = self.InverseScaleFactor,
                        OnComplete = onComplete
                    });
                });

                return true;
            }
        }
    }
}
