#define SMOOTH_NOISE

#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

uniform float  TimeDivisor;
uniform float4 PositionOffset, PositionScale;
uniform float4 VelocityOffset, VelocityScale;
uniform float2 PositionFrequency, VelocityFrequency;
uniform float  OldVelocityWeight;

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

float4 computeFMA(
    float4 oldPosition, float4 oldValue, float4 multiply, float4 add
) {
    float weight = computeWeight(oldPosition);
    return lerp(
        oldValue, (oldValue * multiply) + add, weight * getDeltaTime() / TimeDivisor
    );
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
        randomOffset2 = xy + 1;
    float4 randomP = randomCustomRate(randomOffset1, PositionFrequency);
    float4 randomV = randomCustomRate(randomOffset2, VelocityFrequency);

    float4 positionDelta = (randomP - PositionOffset) * PositionScale;
    float4 velocityDelta = (randomV - VelocityOffset) * VelocityScale;

    newPosition = lerp(oldPosition, oldPosition + positionDelta, t);
    if (OldVelocityWeight >= 0.01)
        newVelocity = lerp(oldVelocity, (oldVelocity * OldVelocityWeight) + velocityDelta, t);
    else
        newVelocity = velocityDelta;
}

technique Noise {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Noise();
    }
}