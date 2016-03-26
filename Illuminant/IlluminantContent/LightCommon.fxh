#include "RampCommon.fxh"

uniform float GroundZ;
uniform float ZToYMultiplier;

uniform float  HeightmapInvScaleFactor;
uniform float2 TerrainTextureTexelSize;

Texture2D TerrainTexture      : register(t2);
sampler TerrainTextureSampler : register(s2) {
    Texture = (TerrainTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

// returns height of the terrain in [0-1] that you should scale
float sampleTerrain(
    float2 positionPx
) {
    float2 uv     = positionPx * TerrainTextureTexelSize;
    float  sample = tex2Dlod(TerrainTextureSampler, float4(uv, 0, 0)).r;
    return sample;
}

float computeLightOpacity(
    float3 shadedPixelPosition, float3 lightCenter,
    float lightRadius, float lightRampLength
) {
    float3 distance3 = shadedPixelPosition - lightCenter;

    float  distance = length(distance3) - lightRadius;
    return 1 - clamp(distance / lightRampLength, 0, 1);
}
