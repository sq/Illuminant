#include "RampCommon.fxh"

uniform float GroundZ;
uniform float ZToYMultiplier;

uniform float  GBufferInvScaleFactor;
uniform float2 GBufferTexelSize;

Texture2D GBuffer      : register(t2);
sampler GBufferSampler : register(s2) {
    Texture = (GBuffer);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

// returns world position data from the gbuffer at the specified screen position
float3 sampleGBuffer(
    float2 screenPositionPx
) {
    // FIXME: Should we be offsetting distance field samples too?
    float2 uv     = (screenPositionPx + 0.5) * GBufferTexelSize;

    float4 sample = tex2Dlod(GBufferSampler, float4(uv, 0, 0));

    return sample.xyz;
}

float computeLightOpacity(
    float3 shadedPixelPosition, float3 lightCenter,
    float lightRadius, float lightRampLength
) {
    float3 distance3 = shadedPixelPosition - lightCenter;

    float  distance = length(distance3) - lightRadius;
    return 1 - clamp(distance / lightRampLength, 0, 1);
}
