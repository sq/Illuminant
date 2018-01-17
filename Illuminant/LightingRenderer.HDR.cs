using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.Render.Tracing;

namespace Squared.Illuminant {
    public struct HDRConfiguration {
        public struct GammaCompressionConfiguration {
            public float MiddleGray, AverageLuminance, MaximumLuminance;
        }

        public struct ToneMappingConfiguration {
            public float Exposure, WhitePoint;
        }

        public HDRMode Mode;
        public float InverseScaleFactor;
        public GammaCompressionConfiguration GammaCompression;
        public ToneMappingConfiguration ToneMapping;
    }

    public enum HDRMode {
        None,
        GammaCompress,
        ToneMap
    }

    public struct LightmapPercentileInfo {
        public float P1, P2, P5, P10, P25, P50, P75, P90, P95, P98, P99;
    }

    public struct LightmapBandInfo {
        public float Minimum, Maximum, Mean;
    }

    public struct LightmapInfo {
        public LightmapPercentileInfo Percentiles;
        public LightmapBandInfo Band;

        public float Minimum, Maximum, Mean;
        public float Overexposed;
    }

    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private const int LuminanceScaleFactor = 8192;
        private const int RedScaleFactor = (int)(0.299 * LuminanceScaleFactor);
        private const int GreenScaleFactor = (int)(0.587 * LuminanceScaleFactor);
        private const int BlueScaleFactor = (int)(0.114 * LuminanceScaleFactor);

        private int[] AnalyzeScratchBuffer;

        private float ComputePercentile (float percentage, int[] buffer, int lastZero, int count, float effectiveScaleFactor) {
            count -= lastZero;
            var index = (int)(count * percentage / 100f);
            if (index < 0)
                index = 0;
            if (index >= count)
                index = count - 1;

            var sample = buffer[lastZero + index];
            return sample * effectiveScaleFactor;
        }

        private unsafe LightmapInfo AnalyzeLightmap (
            byte[] buffer, int count, 
            float scaleFactor, float threshold,
            float lowBandPercentage, float highBandPercentage
        ) {
            int overThresholdCount = 0, min = int.MaxValue, max = 0;
            long sum = 0;

            int pixelCount = count / 
                (Configuration.HighQuality
                    ? 8
                    : 4);

            if ((AnalyzeScratchBuffer == null) || (AnalyzeScratchBuffer.Length < pixelCount))
                AnalyzeScratchBuffer = new int[pixelCount];

            int luminanceThreshold = (int)(threshold * LuminanceScaleFactor / scaleFactor);

            fixed (byte* pBuffer = buffer)
            if (Configuration.HighQuality) {
                var pRgba = (ushort*)pBuffer;

                for (int i = 0; i < pixelCount; i++, pRgba += 4) {
                    int luminance = ((pRgba[0] * RedScaleFactor) + (pRgba[1] * GreenScaleFactor) + (pRgba[2] * BlueScaleFactor)) / 65536;
                    AnalyzeScratchBuffer[i] = luminance;

                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                    sum += luminance;

                    if (luminance >= luminanceThreshold)
                        overThresholdCount += 1;
                }                
            } else {
                var pRgba = pBuffer;

                for (int i = 0; i < pixelCount; i++, pRgba += 4) {
                    int luminance = ((pRgba[0] * 257 * RedScaleFactor) + (pRgba[1] * 257 * GreenScaleFactor) + (pRgba[2] * 257 * BlueScaleFactor)) / 65536;
                    AnalyzeScratchBuffer[i] = luminance;

                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                    sum += luminance;

                    if (luminance >= luminanceThreshold)
                        overThresholdCount += 1;
                }                
            }

            var effectiveScaleFactor = (1.0f / LuminanceScaleFactor) * scaleFactor;

            var scratch = AnalyzeScratchBuffer;
            Array.Sort(scratch, 0, pixelCount);
            var lastZero = Array.LastIndexOf(scratch, 0);

            var percentiles = new LightmapPercentileInfo {
                P1 = ComputePercentile(1, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P2 = ComputePercentile(2, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P5 = ComputePercentile(5, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P10 = ComputePercentile(10, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P25 = ComputePercentile(25, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P50 = ComputePercentile(50, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P75 = ComputePercentile(75, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P90 = ComputePercentile(90, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P95 = ComputePercentile(95, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P98 = ComputePercentile(98, scratch, lastZero, pixelCount, effectiveScaleFactor),
                P99 = ComputePercentile(99, scratch, lastZero, pixelCount, effectiveScaleFactor)
            };

            var result = new LightmapInfo {
                Percentiles = percentiles,
                Overexposed = overThresholdCount / (float)pixelCount,
                Minimum = min * effectiveScaleFactor,
                Maximum = max * effectiveScaleFactor,
                Mean = (sum * effectiveScaleFactor) / pixelCount
            };

            int bandCount = 0, 
                bandMin = (int)ComputePercentile(lowBandPercentage, scratch, lastZero, pixelCount, 1),
                bandMax = (int)ComputePercentile(highBandPercentage, scratch, lastZero, pixelCount, 1);
            min = int.MaxValue;
            max = 0;
            sum = 0;

            for (int i = lastZero; i < pixelCount; i++) {
                var luminance = scratch[i];
                if ((luminance < bandMin) || (luminance > bandMax))
                    continue;
                min = Math.Min(min, luminance);
                max = Math.Max(max, luminance);
                sum += luminance;
                bandCount++;
            }

            if (min == int.MaxValue)
                min = 0;

            result.Band = new LightmapBandInfo {
                Minimum = min * effectiveScaleFactor,
                Maximum = max * effectiveScaleFactor,
                Mean = (sum * effectiveScaleFactor / bandCount)
            };

            return result;
        }

        /// <summary>
        /// Analyzes the internal lighting buffer. This operation is asynchronous so that you do not stall on
        ///  a previous/in-flight draw operation.
        /// </summary>
        /// <param name="scaleFactor">Scale factor for the lighting values (you want 1.0f / intensityFactor, probably)</param>
        /// <param name="threshold">Threshold for overexposed values (after scaling). 1.0f is reasonable.</param>
        /// <param name="accuracyFactor">Governs how many pixels will be analyzed. Higher values are lower accuracy (but faster).</param>
        /// <returns>LightmapInfo containing the minimum, average, and maximum light values, along with an overexposed pixel ratio [0-1].</returns>
        public void EstimateBrightness (
            Action<LightmapInfo> onComplete,
            float scaleFactor, float threshold, 
            float lowBandPercentage = 70,
            float highBandPercentage = 90,
            int accuracyFactor = 3
        ) {
            if (!Configuration.EnableBrightnessEstimation)
                throw new InvalidOperationException("Brightness estimation must be enabled");

            var levelIndex = Math.Min(accuracyFactor, _PreviousLightmap.LevelCount - 1);
            var divisor = (int)Math.Pow(2, levelIndex);
            var levelWidth = _PreviousLightmap.Width / divisor;
            var levelHeight = _PreviousLightmap.Height / divisor;
            var count = levelWidth * levelHeight * 
                (Configuration.HighQuality
                    ? 8
                    : 4);

            if (_ReadbackBuffer == null)
                _ReadbackBuffer = new byte[count];

            Coordinator.AfterPresent(() => {
                _PreviousLightmap.GetData(
                    levelIndex, null,
                    _ReadbackBuffer, 0, count
                );

                var result = AnalyzeLightmap(
                    _ReadbackBuffer, count, scaleFactor, threshold,
                    lowBandPercentage, highBandPercentage
                );
                onComplete(result);
            });
        }
    }
}
