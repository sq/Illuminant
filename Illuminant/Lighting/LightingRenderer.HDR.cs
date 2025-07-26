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
using Squared.Game;
using Squared.Render;
using Squared.Render.Evil;
using Squared.Render.Tracing;
using Squared.Threading;
using Squared.Util;

namespace Squared.Illuminant {
    public sealed partial class LightingRenderer : IDisposable, INameableGraphicsObject {
#if FNA
        private struct HistogramUpdateTask : IMainThreadWorkItem {
#else
        private struct HistogramUpdateTask : IWorkItem {
#endif
            public LightingRenderer Renderer;
            public RenderTarget2D Texture;
            public int LevelIndex;
            public Histogram Histogram;
            public float ScaleFactor;
            public int Width, Height;
            public Action<Histogram> OnComplete;

            public void Execute (ThreadGroup group) {
                var count = Width * Height;

                lock (Renderer._LuminanceReadbackArrayLock) {
                    var buffer = Renderer._LuminanceReadbackArray;
                    if ((buffer == null) || (buffer.Length < count))
                        buffer = Renderer._LuminanceReadbackArray = new float[count];

                    Texture.GetDataFast(
                        LevelIndex, new Rectangle(0, 0, Width, Height),
                        buffer, 0, count
                    );

                    Histogram.Lock.EnterWriteLock();
                    try {
                        Histogram.Clear();
                        Histogram.Add(buffer, count, ScaleFactor);
                    } finally {
                        Histogram.Lock.ExitWriteLock();
                    }
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

        public struct RenderedLighting : IDisposable {
            public   readonly LightingRenderer Renderer;
            public   readonly float            InverseScaleFactor;
            public   readonly RenderTarget2D   Lightmap;
            private  readonly RenderTarget2D   LightProbeValues;
            internal          RenderTarget2D   LuminanceBuffer;
            private  readonly int              Width, Height;
            internal          BatchGroup       BatchGroup;

            private bool IsBatchGroupDisposed;

            internal RenderedLighting (
                LightingRenderer renderer, RenderTarget2D lightmap, float inverseScaleFactor,
                RenderTarget2D lightProbeValues
            ) {
                Renderer = renderer;
                Lightmap = lightmap;
                LightProbeValues = lightProbeValues;
                LuminanceBuffer = null;
                InverseScaleFactor = inverseScaleFactor;
                renderer.Configuration.GetRenderSize(out Width, out Height);
                BatchGroup = null;
                IsBatchGroupDisposed = false;
            }

            public bool IsValid {
                get {
                    return (Renderer != null) && (Lightmap != null);
                }
            }

            public void Resolve (
                IBatchContainer container, int layer,
                float width, float height, Texture2D albedo = null,
                Bounds? albedoTextureRegion = null, SamplerState albedoSamplerState = null, SamplerState lightmapSamplerState = null,
                Vector2? uvOffset = null, HDRConfiguration? hdr = null, 
                LUTBlendingConfiguration? lutBlending = null,
                BlendState blendState = null, bool worldSpace = false
            ) {
                if (!IsValid)
                    throw new InvalidOperationException("Invalid");

                if (!IsBatchGroupDisposed) {
                    IsBatchGroupDisposed = true;
                    BatchGroup.Dispose();
                }

                var rw = (albedo != null) ? albedo.Width : Lightmap.Width;
                var rh = (albedo != null) ? albedo.Height : Lightmap.Height;
                var scale = new Vector2(width / rw, height / rh);

                Renderer.ResolveLighting(
                    container, layer,
                    Lightmap, Vector2.Zero, scale, 
                    albedo, albedoTextureRegion, albedoSamplerState, lightmapSamplerState,
                    uvOffset.GetValueOrDefault(Vector2.Zero), hdr, lutBlending,
                    blendState, worldSpace
                );
            }

            public void Resolve (
                IBatchContainer container, int layer,
                Vector2 position, Vector2? scale = null, Texture2D albedo = null,
                Bounds? albedoTextureRegion = null, SamplerState albedoSamplerState = null, SamplerState lightmapSamplerState = null,
                Vector2? uvOffset = null, HDRConfiguration? hdr = null, 
                LUTBlendingConfiguration? lutBlending = null,
                BlendState blendState = null, bool worldSpace = false
            ) {
                if (!IsValid)
                    throw new InvalidOperationException("Invalid");

                if (!IsBatchGroupDisposed) {
                    IsBatchGroupDisposed = true;
                    BatchGroup.Dispose();
                }

                Renderer.ResolveLighting(
                    container, layer,
                    Lightmap, position, scale.GetValueOrDefault(Vector2.One),
                    albedo, albedoTextureRegion, albedoSamplerState, lightmapSamplerState,
                    uvOffset.GetValueOrDefault(Vector2.Zero), hdr, lutBlending,
                    blendState, worldSpace
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

                var self = this;

                Renderer.Coordinator.ThreadGroup.Enqueue(new HistogramUpdateTask {
                    Renderer = Renderer,
                    Texture = self.LuminanceBuffer,
                    LevelIndex = levelIndex,
                    Histogram = histogram,
                    Width = self.Width / 2 / divisor,
                    Height = self.Height / 2 / divisor,
                    ScaleFactor = self.InverseScaleFactor,
                    OnComplete = onComplete
                });

                return true;
            }

            public IBatchContainer DangerousGetBatchContainer () {
                return BatchGroup;
            }

            public void Dispose () {
                if (!IsBatchGroupDisposed && (BatchGroup != null)) {
                    IsBatchGroupDisposed = true;
                    BatchGroup.Dispose();
                }
            }
        }
    }

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
        public DitheringSettings? Dithering;
        private bool? _ResolveToSRGB, _AlbedoIsSRGB;

        private float ExposureMinusOne, GammaMinusOne;

        public bool AlbedoIsSRGB {
            get {
                return _AlbedoIsSRGB.GetValueOrDefault(false);
            }
            set {
                _AlbedoIsSRGB = value;
            }
        }

        public bool ResolveToSRGB {
            get {
                return _ResolveToSRGB.GetValueOrDefault(false);
            }
            set {
                _ResolveToSRGB = value;
            }
        }

        public float Exposure {
            get {
                return ExposureMinusOne + 1;
            }
            set {
                ExposureMinusOne = value - 1;
            }
        }

        public float Gamma {
            get {
                return GammaMinusOne + 1;
            }
            set {
                GammaMinusOne = value - 1;
            }
        }
    }

    public enum HDRMode {
        None,
        GammaCompress,
        ToneMap
    }    

    public struct LUTBlendingConfiguration {
        public ColorLUT DarkLUT, BrightLUT;
        public bool PerChannel, LUTOnly;
        public float DarkLevel, NeutralBandSize;
        private float _BrightLevelMinus1;
        public float BrightLevel {
            get {
                return _BrightLevelMinus1 + 1;
            }
            set {
                _BrightLevelMinus1 = value - 1;
            }
        }
    }
}
