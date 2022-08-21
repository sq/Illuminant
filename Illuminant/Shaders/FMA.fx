#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform const float TimeDivisor;
uniform const float4 PositionAdd, PositionMultiply;
uniform const float4 VelocityAdd, VelocityMultiply;

uniform const float  Strength;
uniform const int    AreaType;
uniform const float3 AreaCenter, AreaSize;
uniform const float  AreaFalloff, AreaRotation;

uniform const float2 CategoryFilter;

float computeWeight (float3 worldPosition) {
    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize, AreaRotation
    );
    return (1 - saturate(distance / AreaFalloff)) * Strength;
}

float4 computeFMA (
    float4 oldPosition, float4 oldValue, float4 multiply, float4 add
) {
    float weight = computeWeight(oldPosition);
    return lerp(
        oldValue, (oldValue * multiply) + add, weight * getDeltaTime() / TimeDivisor
    );
}

void PS_FMA (
    ACCEPTS_VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1
) {
    float2 xy = GET_VPOS;

    float4 oldPosition, oldVelocity;
    readStatePV(
        xy, oldPosition, oldVelocity
    );

    if ((oldPosition.w <= 0) || !checkCategoryFilter(oldVelocity.w, CategoryFilter)) {
        newPosition = oldPosition;
        newVelocity = oldVelocity;
        return;
    }

    newPosition   = computeFMA(oldPosition, oldPosition, PositionMultiply, PositionAdd);
    newVelocity   = computeFMA(oldPosition, oldVelocity, VelocityMultiply, VelocityAdd);
}

technique FMA {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_FMA();
    }
}