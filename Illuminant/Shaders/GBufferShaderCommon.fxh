#include "EnvironmentCommon.fxh"

#define GBUFFER_Z_SCALE 1024
#define GBUFFER_Z_OFFSET 1024

// FIXME: Use the shared header?
uniform const float  SelfOcclusionHack;
uniform const float3 DistanceFieldExtent;

float4 encodeGBufferSample (
    float3 normal, float relativeY, float z, bool dead, bool enableShadows, bool fullbright
) {
    if (dead) {
        return float4(
            0, 0,
            -99999,
            -99999
        );
    } else {
        // HACK: We drop the world x axis and the normal y axis,
        //  and reconstruct those two values when sampling the g-buffer
        return float4(
            any(normal)
                ? encodeNormalSpherical(normal)
                : float2(0, 0), 
            relativeY,
            fullbright
                // for a fullbright pixel we just make the w value total garbage
                ? 99999
                // If shadows are disabled we negate the Z value, and bias it by -1
                // This ensures that shadows can be disabled for a Z of 0
                : (((z + GBUFFER_Z_OFFSET) / GBUFFER_Z_SCALE) * (enableShadows ? 1 : -1)) + (enableShadows ? 0 : -1)
        );
    }
}