#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

uniform float  FrequencyLerp;
uniform float2 NextRandomnessOffset;
uniform float  TimeDivisor;
uniform float4 PositionOffset, PositionScale;
uniform float4 VelocityOffset, VelocityScale;
uniform float  ReplaceOldVelocity;

uniform float2 SpaceScale;

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
    ACCEPTS_VPOS,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1
) {
    float2 xy = GET_VPOS;

    float4 oldPosition, oldVelocity;
    readStatePV(
        xy, oldPosition, oldVelocity
    );

    float weight = computeWeight(oldPosition);
    float t = weight * getDeltaTime() / TimeDivisor;

    float4 randomP1 = randomCustom(xy, RandomnessOffset, RandomnessTexel);
    float4 randomP2 = randomCustom(xy, NextRandomnessOffset, RandomnessTexel);
    float4 randomV1 = randomCustom(xy + float2(2, 1), RandomnessOffset, RandomnessTexel);
    float4 randomV2 = randomCustom(xy + float2(2, 1), NextRandomnessOffset, RandomnessTexel);

    float4 randomP = lerp(randomP1, randomP2, FrequencyLerp);
    float4 randomV = lerp(randomV1, randomV2, FrequencyLerp);

    float4 positionDelta = (randomP - PositionOffset) * PositionScale;
    float4 velocityDelta = (randomV - VelocityOffset) * VelocityScale;

    newPosition = lerp(oldPosition, oldPosition + positionDelta, t);
    if (ReplaceOldVelocity) {
        newVelocity = float4(lerp(oldVelocity.xyz, velocityDelta.xyz, weight), oldVelocity.w);
        newVelocity.xyz += normalize(oldVelocity.xyz) * velocityDelta.w;
    } else {
        newVelocity = float4(lerp(oldVelocity.xyz, oldVelocity.xyz + velocityDelta.xyz, t), oldVelocity.w);
        newVelocity.xyz += normalize(oldVelocity.xyz) * velocityDelta.w;
    }
}

void PS_SpatialNoise(
    ACCEPTS_VPOS,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1
) {
    float2 xy = GET_VPOS;

    float4 oldPosition, oldVelocity;
    readStatePV(
        xy, oldPosition, oldVelocity
    );

    float weight = computeWeight(oldPosition);
    float t = weight * getDeltaTime() / TimeDivisor;

    float2 randomXy = oldPosition.xy;
    float2 rate = SpaceScale;
    float4 randomP1 = smoothRandomCustom(randomXy, RandomnessOffset, rate);
    float4 randomP2 = smoothRandomCustom(randomXy, NextRandomnessOffset, rate);
    float4 randomV1 = smoothRandomCustom(randomXy + float2(2, 1), RandomnessOffset, rate);
    float4 randomV2 = smoothRandomCustom(randomXy + float2(2, 1), NextRandomnessOffset, rate);

    float4 randomP = lerp(randomP1, randomP2, FrequencyLerp);
    float4 randomV = lerp(randomV1, randomV2, FrequencyLerp);

    float4 positionDelta = (randomP - PositionOffset) * PositionScale;
    float4 velocityDelta = (randomV - VelocityOffset) * VelocityScale;

    newPosition = lerp(oldPosition, oldPosition + positionDelta, t);
    if (ReplaceOldVelocity) {
        newVelocity = float4(lerp(oldVelocity.xyz, velocityDelta.xyz, weight), oldVelocity.w);
        newVelocity.xyz += normalize(oldVelocity.xyz) * velocityDelta.w;
    } else {
        newVelocity = float4(lerp(oldVelocity.xyz, oldVelocity.xyz + velocityDelta.xyz, t), oldVelocity.w);
        newVelocity.xyz += normalize(oldVelocity.xyz) * velocityDelta.w;
    }
}

technique Noise {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Noise();
    }
}

technique SpatialNoise {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_SpatialNoise();
    }
}