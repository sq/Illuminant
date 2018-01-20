using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
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
        
        /// <summary>
        /// Analyzes the internal lighting buffer. This operation is asynchronous so that you do not stall on
        ///  a previous/in-flight draw operation.
        /// </summary>
        /// <param name="inverseScaleFactor">Scale factor for the lighting values (you want 1.0f / intensityFactor, probably)</param>
        /// <param name="accuracyFactor">Governs how many pixels will be analyzed. Higher values are lower accuracy (but faster).</param>
        public void EstimateBrightness (
            Histogram histogram,
            Action<Histogram> onComplete,
            float inverseScaleFactor, int accuracyFactor = 3
        ) {
            if (!Configuration.EnableBrightnessEstimation)
                throw new InvalidOperationException("Brightness estimation must be enabled");

            var levelIndex = Math.Min(accuracyFactor, _PreviousLuminance.LevelCount - 1);
            var divisor = (int)Math.Pow(2, levelIndex);
            var levelWidth = _PreviousLuminance.Width / divisor;
            var levelHeight = _PreviousLuminance.Height / divisor;
            var count = levelWidth * levelHeight;

            if ((_ReadbackBuffer == null) || (_ReadbackBuffer.Length < count))
                _ReadbackBuffer = new float[count];

            var q = Coordinator.ThreadGroup.GetQueueForType<HistogramUpdateTask>();
            var rs = Configuration.RenderSize;

            Coordinator.AfterPresent(() => {
                q.WaitUntilDrained();

                q.Enqueue(new HistogramUpdateTask {
                    Texture = _PreviousLuminance,
                    LevelIndex = levelIndex,
                    Histogram = histogram,
                    Buffer = _ReadbackBuffer,
                    Width = rs.First / 2 / divisor,
                    Height = rs.Second / 2 / divisor,
                    ScaleFactor = inverseScaleFactor,
                    OnComplete = onComplete
                });
            });
        }
    }
}
