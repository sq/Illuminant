using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Render.Convenience;
using Squared.Util;

namespace Squared.Illuminant {
    public class RendererConfiguration {
#if FNA
        public const int MaxSurfaceSize = 8192;
#else
        public const int MaxSurfaceSize = 4096;
#endif

        /// <summary>
        /// The maximum width and height of the viewport.
        /// </summary>
        public          Pair<int>  MaximumRenderSize { get; internal set; }

        /// <summary>
        /// The maximum number of light probes to update.
        /// </summary>
        public readonly int        MaximumLightProbeCount;

        /// <summary>
        /// Uses a high-precision g-buffer and internal lightmap.
        /// </summary>
        public readonly bool       HighQuality;

        /// <summary>
        /// Performs a pre-pass to mask out invisible parts of the scene for 
        ///  more efficient rendering
        /// </summary>
        public readonly bool       StencilCulling;

        /// <summary>
        /// Generates downscaled versions of the internal lightmap that the
        ///  renderer can use to estimate the brightness of the scene for HDR.
        /// This property enables use of RenderedLighting.TryComputeHistogram.
        /// </summary>
        public readonly bool       EnableBrightnessEstimation;

        /// <summary>
        /// Determines how large the ring buffers are. Larger ring buffers use
        ///  more memory but reduce the likelihood that draw or readback operations
        ///  will stall waiting on the previous frame.
        /// </summary>
        public readonly int        RingBufferSize;

        /// <summary>
        /// Scales world coordinates when rendering the G-buffer and lightmap.
        /// You can combine this with RenderSize to produce low-resolution lightmaps.
        /// </summary>
        public Vector2   RenderScale    = Vector2.One;

        /// <summary>
        /// The current width of the viewport.
        /// Must not be larger than MaximumRenderSize.
        /// If unspecified, the maximum render size will be used.
        /// </summary>
        public int? RenderWidth { get; internal set; }
        /// <summary>
        /// The current height of the viewport.
        /// Must not be larger than MaximumRenderSize.
        /// If unspecified, the maximum render size will be used.
        /// </summary>
        public int? RenderHeight { get; internal set; }

        /// <summary>
        /// Sets the default ramp texture to use for lights with no ramp texture set.
        /// </summary>
        public Texture2D DefaultRampTexture;

        /// <summary>
        /// Sets the default quality configuration to use for lights with
        ///  no configuration set.
        /// </summary>
        public RendererQualitySettings DefaultQuality = 
            new RendererQualitySettings();

        /// <summary>
        /// The maximum number of distance field slices to update per frame.
        /// Setting this value too high can crash your video driver.
        /// </summary>
        public int  MaximumFieldUpdatesPerFrame = 1;

        /// <summary>
        /// When rendering at a scale below 1.0, coordinates can get misaligned
        ///  due to the low resolution. This option adjusts all your coordinates
        ///  appropriately to try to line up pixels.
        /// NOTE: This is incompatible with using the ComputeViewPositionAndUVOffset
        ///  method, but you probably won't notice.
        /// </summary>
        public bool ScaleCompensation = true;

        /// <summary>
        /// If disabled, the lighting renderer will not maintain or use a g-buffer.
        /// This breaks the TwoPointFiveD feature and disables billboards.
        /// </summary>
        public bool EnableGBuffer     = true;

        /// <summary>
        /// If true, G-buffer reads will be relative to the current ViewTransform position and scale.
        /// This means the g-buffer will scroll with the viewport.
        /// </summary>
        public bool GBufferViewportRelative = false;

        /// <summary>
        /// If disabled, the lighting renderer will regenerate global illumination data every frame.
        /// </summary>
        public bool GICaching    = true;
        
        /// <summary>
        /// Enables 2.5D projection that maps the Z axis to the Y axis.
        /// This also enables painting the front of height volumes.
        /// </summary>
        public bool TwoPointFiveD     = false;
        
        /// <summary>
        /// Enables perspective projection (Z range configured by environment).
        /// This is completely broken
        /// </summary>
        public bool PerspectiveProjection = false;

        /// <summary>
        /// Paints the ground plane into the g-buffer automatically.
        /// If disabled, any area not covered by a height volume or billboard will be
        ///  an empty void that does not receive light.
        /// </summary>
        public bool RenderGroundPlane = true;

        /// <summary>
        /// Hack to enable setting individual pixels as fullbright so they are unaffected by
        ///  the lightmap in the lightmap + albedo resolve mode.
        /// This requires the g-buffer to be enabled and flagging pixels as fullbright is done 
        ///  through writing special data to the g-buffer. (docs for that tbd)
        /// </summary>
        public bool AllowFullbright = false;

        /// <summary>
        /// Used to load lazy texture resources.
        /// </summary>
        public Func<string, Texture2D> RampTextureLoader = null;

        /// <summary>
        /// Biases the mip level used by projector light sources.
        /// A negative bias will produce sharper projected textures at the cost of aliasing,
        ///  while a positive bias will induce blurring.
        /// </summary>
        public float ProjectorMipBias = -0.33f;

        /// <summary>
        /// Block lights this far behind the shaded pixel even if the vector between the light
        ///  and the pixel would normally cause light to be received. Helps suppress incorrect
        ///  light at shallow angles, but may look wrong in 2.5D.
        /// </summary>
        public float LightOcclusion = 0f;

        public RendererConfiguration (
            int maxWidth, int maxHeight, bool highQuality,
            bool enableBrightnessEstimation = false,
            bool stencilCulling = false,
            int ringBufferSize = 2,
            int maximumLightProbeCount = 256
        ) {
            HighQuality = highQuality;
            StencilCulling = stencilCulling;
            AdjustMaximumRenderSize(maxWidth, maxHeight);
            EnableBrightnessEstimation = enableBrightnessEstimation;
            RingBufferSize = ringBufferSize;

            // HACK: Texture coordinates get all mangled if these values aren't powers of two. Ugh.
            MaximumLightProbeCount = (int)Math.Pow(2, Math.Ceiling(Math.Log(maximumLightProbeCount, 2)));

            if (MaximumLightProbeCount > 2048)
                throw new ArgumentException("Maximum light probe count is 2048");
            if (MaximumLightProbeCount < 16)
                MaximumLightProbeCount = 16;
        }

        public void SetScale (float scaleRatio, int? width = null, int? height = null) {
            var maxWidth = width.GetValueOrDefault(MaximumRenderSize.First);
            var maxHeight = height.GetValueOrDefault(MaximumRenderSize.Second);
            var widthPixels = (int)Math.Round(maxWidth * scaleRatio);
            var heightPixels = (int)Math.Round(maxHeight * scaleRatio);
            var scale = new Vector2(
                widthPixels / (float)maxWidth,
                heightPixels / (float)maxHeight
            );
            RenderScale = scale;
            SetRenderSize(widthPixels, heightPixels);
        }

        /// <summary>
        /// Adjusts the maximum render size. This will cause lighting renderers
        ///  to recreate their surfaces. Note that you need to update RenderSize and
        ///  RenderScale afterwards if they were being used.
        /// </summary>
        /// <param name="newWidth"></param>
        /// <param name="newHeight"></param>
        public void AdjustMaximumRenderSize (int newWidth, int newHeight) {
            var oldSize = MaximumRenderSize;

            if (newWidth <= 0 || newWidth > MaxSurfaceSize)
                throw new ArgumentOutOfRangeException("newWidth");
            if (newHeight <= 0 || newHeight > MaxSurfaceSize)
                throw new ArgumentOutOfRangeException("newHeight");

            MaximumRenderSize = new Pair<int>(newWidth, newHeight);
        }

        public void SetRenderSize (int? newWidth, int? newHeight) {
            if (newWidth.HasValue) {
                if ((newWidth.Value <= 0) || (newWidth.Value > MaximumRenderSize.First))
                    throw new ArgumentOutOfRangeException("newWidth");
            }
            if (newHeight.HasValue) {
                if ((newHeight.Value <= 0) || (newHeight.Value > MaximumRenderSize.Second))
                    throw new ArgumentOutOfRangeException("newHeight");
            }

            RenderWidth = newWidth;
            RenderHeight = newHeight;
        }

        public void GetRenderSize (out int width, out int height) {
            width = RenderWidth ?? MaximumRenderSize.First;
            height = RenderHeight ?? MaximumRenderSize.Second;
        }
    }

    public class RendererQualitySettings {
        /// <summary>
        /// Individual cone trace steps are not allowed to be any shorter than this.
        /// Improves the worst-case performance of the trace and avoids spending forever
        ///  stepping short distances around the edges of objects.
        /// Setting this to 1 produces the 'best' results but larger values tend to look
        ///  just fine. If this is too high you will get banding artifacts.
        /// </summary>
        public float MinStepSize             = 3.0f;
        /// <summary>
        /// Long step distances are scaled by this factor. A factor below 1.0
        ///  eliminates banding artifacts in the soft area between full/no shadow,
        ///  at the cost of additional cone trace steps.
        /// This effectively increases how much time we spend outside of objects,
        ///  producing higher quality as a side effect.
        /// Only set this above 1.0 if you love goofy looking artifacts
        /// </summary>
        public float LongStepFactor          = 1.0f;
        /// <summary>
        /// Terminates a cone trace after this many steps.
        /// Mitigates the performance hit for complex traces near the edge of objects.
        /// Most traces will not hit this cap.
        /// </summary>
        public int   MaxStepCount            = 64;
        /// <summary>
        /// As the cone trace proceeds the size of the cone expands, producing a softer
        ///  shadow. This setting limits how large the cone can become.
        /// </summary>
        public float MaxConeRadius           = 24;
        /// <summary>
        /// A lower cone growth factor causes the shadow cone to grow more slowly as the
        ///  trace proceeds through the world.
        /// </summary>
        public float ConeGrowthFactor        = 1.0f;
        /// <summary>
        /// An exponent that adjusts how sharp the transition between light and shadow is.
        /// </summary>
        public float OcclusionToOpacityPower = 1;
    }
}
