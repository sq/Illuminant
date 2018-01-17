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

    public struct LightmapBandInfo {
        public float Minimum, Maximum, Mean;
    }

    public struct LightmapInfo {
        public LightmapBandInfo Band;

        public float Minimum, Maximum, Mean;
        public float Overexposed;
    }

    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
        private float ComputePercentile (float percentage, float[] buffer, int lastZero, int count, float effectiveScaleFactor) {
            count -= lastZero;
            var index = (int)(count * percentage / 100f);
            if (index < 0)
                index = 0;
            if (index >= count)
                index = count - 1;

            return buffer[lastZero + index];
        }

        private unsafe LightmapInfo AnalyzeLightmap (
            float[] buffer, int count, 
            float scaleFactor, float threshold,
            float lowBandPercentage, float highBandPercentage
        ) {
            int overThresholdCount = 0;
            float min = float.MaxValue, max = 0;
            float sum = 0;

            fixed (float* pBuffer = buffer) {
                for (int i = 0; i < count; i++) {
                    var luminance = pBuffer[i] * scaleFactor;

                    min = Math.Min(min, luminance);
                    max = Math.Max(max, luminance);
                    sum += luminance;

                    if (luminance >= threshold)
                        overThresholdCount += 1;
                }                
            }

            var result = new LightmapInfo {
                Overexposed = overThresholdCount / (float)count,
                Minimum = min,
                Maximum = max,
                Mean = sum / count
            };

            Array.Sort(buffer, 0, count);

            var lastZero = Array.LastIndexOf(buffer, 0);
            if (lastZero < 0)
                lastZero = 0;

            int bandCount = 0;
            float bandMin = ComputePercentile(lowBandPercentage, buffer, lastZero, count, 1) * scaleFactor,
                bandMax = ComputePercentile(highBandPercentage, buffer, lastZero, count, 1) * scaleFactor;
            min = float.MaxValue;
            max = 0;
            sum = 0;

            for (int i = lastZero; i < count; i++) {
                var luminance = buffer[i] * scaleFactor;
                if ((luminance < bandMin) || (luminance > bandMax))
                    continue;
                min = Math.Min(min, luminance);
                max = Math.Max(max, luminance);
                sum += luminance;
                bandCount++;
            }

            if (min == float.MaxValue)
                min = 0;

            result.Band = new LightmapBandInfo {
                Minimum = min,
                Maximum = max,
                Mean = sum / bandCount
            };

            return result;
        }

        /// <summary>
        /// Analyzes the internal lighting buffer. This operation is asynchronous so that you do not stall on
        ///  a previous/in-flight draw operation.
        /// </summary>
        /// <param name="inverseScaleFactor">Scale factor for the lighting values (you want 1.0f / intensityFactor, probably)</param>
        /// <param name="threshold">Threshold for overexposed values (after scaling). 1.0f is reasonable.</param>
        /// <param name="accuracyFactor">Governs how many pixels will be analyzed. Higher values are lower accuracy (but faster).</param>
        /// <returns>LightmapInfo containing the minimum, average, and maximum light values, along with an overexposed pixel ratio [0-1].</returns>
        public void EstimateBrightness (
            Action<LightmapInfo> onComplete,
            float inverseScaleFactor, float threshold, 
            float lowBandPercentage = 70,
            float highBandPercentage = 90,
            int accuracyFactor = 3
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

            Coordinator.AfterPresent(() => {
                _PreviousLuminance.GetData(
                    levelIndex, null,
                    _ReadbackBuffer, 0, count
                );

                var result = AnalyzeLightmap(
                    _ReadbackBuffer, count, inverseScaleFactor, threshold,
                    lowBandPercentage, highBandPercentage
                );
                onComplete(result);
            });
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

            Coordinator.AfterPresent(() => {
                _PreviousLuminance.GetData(
                    levelIndex, null,
                    _ReadbackBuffer, 0, count
                );

                lock (histogram) {
                    histogram.Clear();
                    histogram.Add(_ReadbackBuffer, count, inverseScaleFactor);
                }

                if (onComplete != null)
                    onComplete(histogram);
            });
        }
    }

    public class Histogram {
        public struct Bucket {
            public float BucketStart, BucketEnd;
            public float Min, Max, Mean;
            public int Count;
        }

        private struct BucketState {
            public int Count;
            public float Min, Max, Sum;
        }

        public const int BucketCount = 64;

        private readonly float[] BucketMaxValues;
        private readonly BucketState[] States;

        public int SampleCount, LargestBucketCount;
        public float Min, Max, Mean, Sum;

        public Histogram (float maxValue, float power) {
            BucketMaxValues = new float[BucketCount];
            States = new BucketState[BucketCount];

            var maxValuePlusOneLog = Math.Log(1 + maxValue, power);

            for (int i = 0; i < BucketCount; i++) {
                var valueLog = (maxValuePlusOneLog / BucketCount) * (i + 1);
                var value = (float)Math.Pow(power, valueLog) - 1;
                BucketMaxValues[i] = value;
            }

            Clear();
        }

        public void Clear () {
            SampleCount = 0;
            Min = float.MaxValue;
            Max = 0;
            Sum = 0;
            Mean = 0;

            for (int i = 0; i < BucketCount; i++) {
                States[i] = default(BucketState);
                States[i].Min = float.MaxValue;
            }
        }

        public void Add (float[] buffer, int count, float scaleFactor) {
            for (int i = 0; i < count; i++) {
                var value = buffer[i] * scaleFactor;

                Min = Math.Min(Min, value);
                Max = Math.Max(Max, value);
                Sum += value;

                for (int j = 0; j < BucketCount; j++) {
                    if (BucketMaxValues[j] > value) {
                        States[j].Count++;
                        States[j].Sum += value;
                        States[j].Min = Math.Min(States[j].Min, value);
                        States[j].Max = Math.Max(States[j].Max, value);
                        break;
                    }
                }
            }
            
            SampleCount += count;
            Mean = Sum / SampleCount;
        }

        public IEnumerable<Bucket> Buckets {
            get {
                float minValue;
                for (int i = 0; i < BucketCount; i++) {
                    if (i > 0)
                        minValue = BucketMaxValues[i - 1];
                    else
                        minValue = 0;

                    var state = States[i];

                    yield return new Bucket {
                        BucketStart = minValue,
                        BucketEnd = BucketMaxValues[i],
                        Count = state.Count,
                        Min = state.Min,
                        Max = state.Max,
                        Mean = state.Sum / state.Count
                    };
                }
            }
        }
    }
}
