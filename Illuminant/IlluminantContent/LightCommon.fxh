#include "RampCommon.fxh"

uniform float  ZDistanceScale;

uniform float2 TerrainTextureTexelSize;

Texture2D TerrainTexture      : register(t2);
sampler TerrainTextureSampler : register(s2) {
    Texture = (TerrainTexture);
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

float computeLightOpacity(
    float3 shadedPixelPosition, float3 lightCenter,
    float rampStart, float rampEnd
) {
    float3 distance3 = shadedPixelPosition - lightCenter;
    distance3.z *= ZDistanceScale;

    float  distance = length(distance3) - rampStart;
    return 1 - clamp(distance / (rampEnd - rampStart), 0, 1);
}
