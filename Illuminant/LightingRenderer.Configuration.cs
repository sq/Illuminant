using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Util;

namespace Squared.Illuminant {
    public class RendererConfiguration {
        /// <summary>
        /// The maximum width and height of the viewport.
        /// </summary>
        public readonly Pair<int> MaximumRenderSize;

        /// <summary>
        /// The maximum number of light probes to update.
        /// </summary>
        public readonly int       MaximumLightProbeCount;

        /// <summary>
        /// The maximum number of global illumination probes to update.
        /// </summary>
        public readonly int       MaximumGIProbeCount;

        /// <summary>
        /// Determines how many samples contribute to the value of each GI probe.
        /// </summary>
        public readonly GIProbeQualityLevels 
                                  GIProbeQualityLevel;

        /// <summary>
        /// Uses a high-precision g-buffer and internal lightmap.
        /// </summary>
        public readonly bool      HighQuality;

        /// <summary>
        /// Generates downscaled versions of the internal lightmap that the
        ///  renderer can use to estimate the brightness of the scene for HDR.
        /// This property enables use of RenderedLighting.TryComputeHistogram.
        /// </summary>
        public readonly bool      EnableBrightnessEstimation;

        public readonly bool      EnableGlobalIllumination;

        /// <summary>
        /// Determines how large the ring buffers are. Larger ring buffers use
        ///  more memory but reduce the likelihood that draw or readback operations
        ///  will stall waiting on the previous frame.
        /// </summary>
        public readonly int       RingBufferSize;

        /// <summary>
        /// Scales world coordinates when rendering the G-buffer and lightmap.
        /// You can combine this with RenderSize to produce low-resolution lightmaps.
        /// </summary>
        public Vector2   RenderScale    = Vector2.One;

        /// <summary>
        /// The current width and height of the viewport.
        /// Must not be larger than MaximumRenderSize.
        /// </summary>
        public Pair<int> RenderSize;

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

        public RendererQualitySettings GIProbeQuality =
            new RendererQualitySettings();
    
        /// <summary>
        /// The maximum distance that GI probe selection will search for surfaces.
        /// </summary>
        public float GIBounceSearchDistance = 1024;

        /// <summary>
        /// The distance at which GI probe light values fade to black.
        /// </summary>
        public float GIBounceFalloffDistance = 800;

        /// <summary>
        /// The distance at which a GI probe stops contributing light.
        /// </summary>
        public float GIRadianceFalloffDistance = 100;

        /// <summary>
        /// The maximum number of distance field slices to update per frame.
        /// Setting this value too high can crash your video driver.
        /// </summary>
        public int  MaxFieldUpdatesPerFrame = 1;

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
        /// If disabled, the lighting renderer will regenerate its g-buffer every frame.
        /// </summary>
        public bool GBufferCaching    = true;
        
        /// <summary>
        /// Enables 2.5D projection that maps the Z axis to the Y axis.
        /// This also enables painting the front of height volumes.
        /// </summary>
        public bool TwoPointFiveD     = false;

        /// <summary>
        /// Paints the ground plane into the g-buffer automatically.
        /// If disabled, any area not covered by a height volume or billboard will be
        ///  an empty void that does not receive light.
        /// </summary>
        public bool RenderGroundPlane = true;

        public RendererConfiguration (
            int maxWidth, int maxHeight, bool highQuality,
            bool enableBrightnessEstimation = false,
            bool enableGlobalIllumination = false,
            int ringBufferSize = 2,
            int maximumLightProbeCount = 256,
            int maximumGIProbeCount = 1024,
            GIProbeQualityLevels giProbeQualityLevel = GIProbeQualityLevels.Medium
        ) {
            HighQuality = highQuality;
            MaximumRenderSize = new Pair<int>(maxWidth, maxHeight);
            RenderSize = MaximumRenderSize;
            EnableBrightnessEstimation = enableBrightnessEstimation;
            EnableGlobalIllumination = enableGlobalIllumination;
            RingBufferSize = ringBufferSize;

            // HACK: Texture coordinates get all mangled if these values aren't powers of two. Ugh.
            MaximumLightProbeCount = (int)Math.Pow(2, Math.Ceiling(Math.Log(maximumLightProbeCount, 2)));
            MaximumGIProbeCount = (int)Math.Pow(2, Math.Ceiling(Math.Log(maximumGIProbeCount, 2)));
            GIProbeQualityLevel = giProbeQualityLevel;

            if (MaximumLightProbeCount > 2048)
                throw new ArgumentException("Maximum light probe count is 2048");
            if (MaximumGIProbeCount > 2048)
                throw new ArgumentException("Maximum GI probe count is 2048");
            if (MaximumLightProbeCount < 16)
                MaximumLightProbeCount = 16;
            if (MaximumGIProbeCount < 16)
                MaximumGIProbeCount = 16;
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
