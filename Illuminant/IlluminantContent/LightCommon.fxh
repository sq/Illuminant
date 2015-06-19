#include "RampCommon.fxh"

uniform float  ZDistanceScale;

float computeLightOpacity(
    float3 shadedPixelPosition, float3 lightCenter,
    float rampStart, float rampEnd
) {
    float3 distance3 = shadedPixelPosition - lightCenter;
    distance3.z *= ZDistanceScale;

    float  distance = length(distance3) - rampStart;
    return 1 - clamp(distance / (rampEnd - rampStart), 0, 1);
}
