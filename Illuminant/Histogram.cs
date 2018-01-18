using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Game;
using Squared.Render;
using Squared.Render.Convenience;

namespace Squared.Illuminant {

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

    public class HistogramVisualizer {
        public float SampleCountPower = 3;
        public Color BorderColor = Color.White, BackgroundColor = Color.Black * 0.66f;
        public DefaultMaterialSet Materials;
        public Bounds Bounds;

        public void Draw (IBatchContainer group, int layer, Histogram h) {
            var ir = new ImperativeRenderer(group, Materials, layer).MakeSubgroup();
            ir.AutoIncrementLayer = true;

            ir.FillRectangle(Bounds, BackgroundColor, blendState: BlendState.AlphaBlend);
            ir.OutlineRectangle(Bounds, BorderColor);

            int i = 0;
            float x1 = Bounds.TopLeft.X;
            float bucketWidth = Bounds.Size.X / Histogram.BucketCount;

            ir.AutoIncrementLayer = false;

            lock (h) {
                double maxCount = h.SampleCount;
                double logMaxCount = Math.Log(h.SampleCount + 1, SampleCountPower);
                foreach (var bucket in h.Buckets) {
                    var scaledLogCount = Math.Log(bucket.Count + 1, SampleCountPower) / logMaxCount;
                    var scaledCount = bucket.Count / maxCount;
                    var y2 = Bounds.BottomRight.Y;
                    var y1 = y2 - (float)((scaledCount + scaledLogCount) * 0.5 * Bounds.Size.Y);
                    var x2 = x1 + bucketWidth;

                    ir.FillRectangle(
                        new Bounds(new Vector2(x1, y1), new Vector2(x2, y2)), 
                        Color.Silver
                    ); 

                    x1 = x2;
                    i++;
                }
            }
        }
    }
}
