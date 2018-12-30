#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

uniform float  FrequencyLerp;
uniform float2 NextRandomnessOffset;
uniform float  TimeDivisor;
uniform float4 PositionOffset, PositionScale;
uniform float4 VelocityOffset, VelocityScale;
uniform bool   ReplaceOldVelocity;

uniform float  Strength;
uniform int    AreaType;
uniform float3 AreaCenter, AreaSize;
uniform float  AreaFalloff;

float computeWeight(float3 worldPosition) {
    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize
    );
    return (1 - saturate(distance / AreaFalloff)) * Strength;
}

void PS_Noise(
    in  float2 xy            : VPOS,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1
) {
    float4 oldPosition, oldVelocity;
    readStatePV(
        xy, oldPosition, oldVelocity
    );

    float weight = computeWeight(oldPosition);
    float t = weight * getDeltaTime() / TimeDivisor;

    float2 randomOffset1 = xy, 
        // FIXME: Correlated in a way that might be noticeable?
        randomOffset2 = xy + (NextRandomnessOffset - RandomnessOffset);
    float4 randomP1 = random(randomOffset1);
    float4 randomP2 = random(randomOffset2);
    float4 randomV1 = random(randomOffset1 + float2(2, 1));
    float4 randomV2 = random(randomOffset2 + float2(2, 1));

    float4 randomP = lerp(randomP1, randomP2, FrequencyLerp);
    float4 randomV = lerp(randomV1, randomV2, FrequencyLerp);

    float4 positionDelta = (randomP - PositionOffset) * PositionScale;
    float4 velocityDelta = (randomV - VelocityOffset) * VelocityScale;

    newPosition = lerp(oldPosition, oldPosition + positionDelta, t);
    if (ReplaceOldVelocity)
        newVelocity = lerp(oldVelocity, velocityDelta, weight);
    else
        newVelocity = lerp(oldVelocity, oldVelocity + velocityDelta, t);
}

technique Noise {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Noise();
    }
}