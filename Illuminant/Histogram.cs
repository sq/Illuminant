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
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.Illuminant {
    public unsafe class Histogram : IDisposable {
        private struct FloatComparer : IComparer<float> {
            public static readonly FloatComparer Instance = new FloatComparer();

            public int Compare (float lhs, float rhs) {
                return lhs.CompareTo(rhs);
            }
        }

        public struct Bucket {
            public float BucketStart, BucketEnd;
            public float Min, Max, Mean;
            public int Count;
        }

        private struct BucketState {
            public int Count;
            public float Min, Max, Sum;
        }

        public readonly int BucketCount;

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
        public float Median { get; private set; }

        public bool IgnoreZeroes;

        public readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();

        private float Sum;

        public Histogram (float maxValue, float power, int bucketCount = 64, bool ignoreZeroes = false) {
            BucketCount = bucketCount;
            MaxInputValue = maxValue;
            IgnoreZeroes = ignoreZeroes;
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

            Clear(false);
        }

        public void Dispose () {
            MaxValuePin.Free();
            StatesPin.Free();
            pMaxValues = null;
            pStates = null;
        }

        public void Clear () {
            Clear(true);
        }

        private void Clear (bool enforceLocking) {
            if (enforceLocking && !Lock.IsWriteLockHeld)
                throw new InvalidOperationException();

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

        public bool GetPercentile (float percent, out int bucketIndex, out float value) {
            if (!Lock.IsReadLockHeld)
                throw new InvalidOperationException();

            if ((SampleCount < 1) || (percent < 0) || (percent > 100)) {
                bucketIndex = 0;
                value = 0;
                return false;
            }

            int sampleIndex = (int)(SampleCount * percent / 100f);

            int bucketFirstSample = 0;
            for (int i = 0; i < BucketCount; i++) {
                var count = States[i].Count;
                var localIndex = sampleIndex - bucketFirstSample;
                if ((localIndex >= 0) && (localIndex < count)) {
                    var minValue = (i > 0) ? pMaxValues[i - 1] : 0f;
                    var maxValue = pMaxValues[i];                    
                    bucketIndex = i;
                    value = Arithmetic.Lerp(minValue, maxValue, (localIndex / (float)count));
                    return true;
                }

                bucketFirstSample += count;
            }

            throw new Exception();
        }

        public void Add (float[] buffer, int count, float scaleFactor) {
            if (!Lock.IsWriteLockHeld)
                throw new InvalidOperationException();

            if (count > buffer.Length)
                throw new ArgumentException("count");

            var totalAdded = 0;

            Array.Sort(buffer, 0, count);

            int medianOffset = 0;
            if (IgnoreZeroes)
                medianOffset = Array.LastIndexOf(buffer, 0);

            var medianIndex = Arithmetic.Clamp(((count - medianOffset) / 2) + medianOffset, 0, count - 1);
            Median = buffer[medianIndex] * scaleFactor;

            fixed (float* pBuffer = buffer)
            for (int i = 0; i < count; i++) {
                var rawValue = pBuffer[i];
                if (IgnoreZeroes && (rawValue <= 0))
                    continue;

                var value = rawValue * scaleFactor;

                Sum += value;
                totalAdded++;

                var j = PickBucketForValue(value);
                var pState = &pStates[j];
                pState->Count++;
                pState->Sum += value;
                pState->Min = Math.Min(pState->Min, value);
                pState->Max = Math.Max(pState->Max, value);
            }
            
            SampleCount += totalAdded;
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
                if (!Lock.IsReadLockHeld)
                    throw new InvalidOperationException();

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

    public class HistogramVisualizer {
        public float SampleCountPower = 3;
        public Color BorderColor = Color.White, BackgroundColor = Color.MidnightBlue * 0.75f;
        public Color PercentileColor = Color.White, RangeColor = Color.White * 0.33f, MedianColor = Color.SpringGreen;
        public float PercentileWidth = 1f;
        public Color[] ValueColors = new[] { Color.Black, Color.White, Color.Yellow, Color.Red };
        public DefaultMaterialSet Materials;
        public Bounds Bounds;

        public void GetRangeFromToneMapParameters (HDRConfiguration hdr, out float min, out float max) {
            min = -hdr.Offset;
            max = (1f / (hdr.Exposure / hdr.ToneMapping.WhitePoint)) - hdr.Offset;
        }

        public void Draw (
            IBatchContainer group, int layer, Histogram h,
            float[] percentiles = null, float? rangeMin = null, float? rangeMax = null
        ) {
            var ir = new ImperativeRenderer(group, Materials, layer, blendState: BlendState.AlphaBlend).MakeSubgroup();
            ir.AutoIncrementLayer = false;

            ir.FillRectangle(Bounds, BackgroundColor, layer: 0);
            ir.OutlineRectangle(Bounds, BorderColor, layer: 4);

            int i = 0;
            float x1 = Bounds.TopLeft.X, x2;

            h.Lock.EnterReadLock();

            try {
                double maxCount = h.SampleCount;
                double logMaxCount = Math.Log(h.SampleCount + 1, SampleCountPower);

                foreach (var bucket in h.Buckets) {
                    float bucketWidth = Bounds.Size.X * (bucket.BucketEnd - bucket.BucketStart) / h.MaxInputValue;
                    var scaledLogCount = Math.Log(bucket.Count + 1, SampleCountPower) / logMaxCount;
                    var scaledCount = bucket.Count / maxCount;
                    var y2 = Bounds.BottomRight.Y;
                    var y1 = y2 - (float)((scaledCount + scaledLogCount) * 0.5 * Bounds.Size.Y);
                    x2 = x1 + bucketWidth;

                    var bucketValue = (bucket.BucketStart + bucket.BucketEnd) / 2f;
                    var lowIndex = Arithmetic.Clamp((int)Math.Floor(bucketValue), 0, ValueColors.Length - 1);
                    var highIndex = Arithmetic.Clamp(lowIndex + 1, 0, ValueColors.Length - 1);
                    var elementColor = Color.Lerp(ValueColors[lowIndex], ValueColors[highIndex], bucketValue - (float)Math.Floor(bucketValue));

                    ir.FillRectangle(
                        new Bounds(new Vector2(x1, y1), new Vector2(x2, y2)), 
                        elementColor, layer: 2
                    ); 

                    x1 = x2;
                    i++;
                }

                if (percentiles != null)
                foreach (var percentile in percentiles) {
                    int bucketIndex;
                    float value;
                    if (!h.GetPercentile(percentile, out bucketIndex, out value))
                        continue;

                    var x = Arithmetic.Lerp(Bounds.TopLeft.X, Bounds.BottomRight.X, Arithmetic.Clamp(value / h.MaxInputValue, 0, 1));
                    var halfWidth = PercentileWidth / 2f;

                    ir.FillRectangle(
                        new Bounds(new Vector2(x - halfWidth, Bounds.TopLeft.Y), new Vector2(x + halfWidth, Bounds.BottomRight.Y)), 
                        PercentileColor, layer: 3
                    );
                }

                {
                    var median = h.Median;
                    var x = Arithmetic.Lerp(Bounds.TopLeft.X, Bounds.BottomRight.X, Arithmetic.Clamp(median / h.MaxInputValue, 0, 1));
                    var halfWidth = PercentileWidth / 2f;

                    ir.FillRectangle(
                        new Bounds(new Vector2(x - halfWidth, Bounds.TopLeft.Y), new Vector2(x + halfWidth, Bounds.BottomRight.Y)), 
                        MedianColor, layer: 3
                    );
                }

                if (rangeMin.HasValue || rangeMax.HasValue) {
                    float min = rangeMin.GetValueOrDefault(0);
                    float max = rangeMax.GetValueOrDefault(h.MaxInputValue);
                    x1 = Arithmetic.Lerp(Bounds.TopLeft.X, Bounds.BottomRight.X, Arithmetic.Clamp(min / h.MaxInputValue, 0, 1));
                    x2 = Arithmetic.Lerp(Bounds.TopLeft.X, Bounds.BottomRight.X, Arithmetic.Clamp(max / h.MaxInputValue, 0, 1));

                    ir.FillRectangle(
                        new Bounds(new Vector2(x1, Bounds.TopLeft.Y), new Vector2(x2, Bounds.BottomRight.Y)),
                        RangeColor, layer: 1
                    );
                }
            } finally {
                h.Lock.ExitReadLock();
            }
        }
    }
}
