#include "EnvironmentCommon.fxh"

#define RELATIVEY_SCALE 128

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
            (normal.x / 2) + 0.5,
            (normal.z / 2) + 0.5,
            (relativeY / RELATIVEY_SCALE),
            fullbright
                // for a fullbright pixel we just make the w value total garbage
                ? 99999
                // If shadows are disabled we negate the Z value, and bias it by -1
                // This ensures that shadows can be disabled for a Z of 0
                : ((z / 512) * (enableShadows ? 1 : -1)) + (enableShadows ? 0 : -1)
        );
    }
}