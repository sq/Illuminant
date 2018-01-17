﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Tracing;
using Squared.Threading;

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
        private struct HistogramUpdateTask : IWorkItem {
            public RenderTarget2D Texture;
            public int LevelIndex;
            public Histogram Histogram;
            public float[] Buffer;
            public float ScaleFactor;
            public int Count;
            public Action<Histogram> OnComplete;

            public void Execute () {
                Texture.GetData(
                    LevelIndex, null,
                    Buffer, 0, Count
                );

                lock (Histogram) {
                    Histogram.Clear();
                    Histogram.Add(Buffer, Count, ScaleFactor);
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

            var q = Coordinator.ThreadGroup.GetQueueForType<HistogramUpdateTask>();

            Coordinator.AfterPresent(() => {
                q.WaitUntilDrained();

                q.Enqueue(new HistogramUpdateTask {
                    Texture = _PreviousLuminance,
                    LevelIndex = levelIndex,
                    Histogram = histogram,
                    Buffer = _ReadbackBuffer,
                    Count = count,
                    ScaleFactor = inverseScaleFactor,
                    OnComplete = onComplete
                });
            });
        }
    }

    public unsafe class Histogram : IDisposable {
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

        public readonly float MaxInputValue;

        private readonly float FirstBucketMaxValue;
        private readonly float LastBucketMinValue;
        private readonly float[] BucketMaxValues;
        private readonly BucketState[] States;

        private GCHandle     MaxValuePin, StatesPin;
        private float*       pMaxValues;
        private BucketState* pStates;

        public int SampleCount { get; private set; }
        public float Min { get; private set; }
        public float Max { get; private set; }
        public float Mean { get; private set; }

        private float Sum;

        public Histogram (float maxValue, float power) {
            MaxInputValue = maxValue;
            BucketMaxValues = new float[BucketCount];
            States = new BucketState[BucketCount];

            var maxValuePlusOneLog = Math.Log(1 + maxValue, power);

            for (int i = 0; i < BucketCount; i++) {
                var valueLog = (maxValuePlusOneLog / BucketCount) * (i + 1);
                var value = (float)Math.Pow(power, valueLog) - 1;
                BucketMaxValues[i] = value;
            }

            FirstBucketMaxValue = BucketMaxValues[0];
            LastBucketMinValue = BucketMaxValues[BucketCount - 2];
            MaxValuePin = GCHandle.Alloc(BucketMaxValues, GCHandleType.Pinned);
            StatesPin = GCHandle.Alloc(States, GCHandleType.Pinned);
            pMaxValues = (float*)MaxValuePin.AddrOfPinnedObject();
            pStates = (BucketState*)StatesPin.AddrOfPinnedObject();

            Clear();
        }

        public void Dispose () {
            MaxValuePin.Free();
            StatesPin.Free();
            pMaxValues = null;
            pStates = null;
        }

        public void Clear () {
            SampleCount = 0;
            Min = 0;
            Max = 0;
            Sum = 0;
            Mean = 0;

            for (int i = 0; i < BucketCount; i++) {
                States[i] = default(BucketState);
                States[i].Min = float.MaxValue;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]        
        private int PickBucketForValue (float value) {
            if (value < FirstBucketMaxValue)
                return 0;
            else if (value >= LastBucketMinValue)
                return BucketCount - 1;

            int i = 0, max = BucketCount - 1;
            while (i <= max) {
				int pivot = i + (max - i >> 1);

                if (pMaxValues[pivot] <= value) {
					i = pivot + 1;
				} else {
				    max = pivot - 1;
				}
			}

			return i;
        }

        public void Add (float[] buffer, int count, float scaleFactor) {
            if (count > buffer.Length)
                throw new ArgumentException("count");

            fixed (float* pBuffer = buffer)
            for (int i = 0; i < count; i++) {
                var value = pBuffer[i] * scaleFactor;

                Sum += value;

                var j = PickBucketForValue(value);
                var pState = &pStates[j];
                pState->Count++;
                pState->Sum += value;
                pState->Min = Math.Min(pState->Min, value);
                pState->Max = Math.Max(pState->Max, value);
            }
            
            SampleCount += count;
            if (SampleCount > 0)
                Mean = Sum / SampleCount;
            else
                Mean = 0;

            float min = float.MaxValue, max = 0;
            for (int j = 0; j < BucketCount; j++) {
                min = Math.Min(States[j].Min, min);
                max = Math.Max(States[j].Max, max);
            }

            if (SampleCount > 0)
                Min = min;
            else
                Min = 0;
            Max = max;
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
                        Min = state.Count > 0 ? state.Min : 0,
                        Max = state.Max,
                        Mean = state.Count > 0 ? state.Sum / state.Count : 0
                    };
                }
            }
        }
    }
}
