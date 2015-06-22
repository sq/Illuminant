#include "RampCommon.fxh"

uniform float GroundZ;
uniform float ZDistanceScale;
uniform float ZToYMultiplier;

uniform float2 TerrainTextureTexelSize;

Texture2D TerrainTexture      : register(t2);
sampler TerrainTextureSampler : register(s2) {
    Texture = (TerrainTexture);
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// returns [min, max]
float2 sampleTerrain(
    float2 positionPx
) {
    float2 uv     = positionPx * TerrainTextureTexelSize;
    float2 sample = tex2Dgrad(TerrainTextureSampler, uv, 0, 0).rg;
    return sample;
}

int isLightInvisible(
    float3 lightCenter
) {
    float2 terrainZ = sampleTerrain(lightCenter.xy);
    return 
        (lightCenter.z < 0) ||
        ((lightCenter.z > terrainZ.x) && (lightCenter.z < terrainZ.y))
    ;
}

float computeLightOpacity(
    float3 shadedPixelPosition, float3 lightCenter,
    float rampStart, float rampEnd
) {
    float3 distance3 = shadedPixelPosition - lightCenter;
    distance3.z *= ZDistanceScale;

    float  distance = length(distance3) - rampStart;
    return 1 - clamp(distance / (rampEnd - rampStart), 0, 1);
}
