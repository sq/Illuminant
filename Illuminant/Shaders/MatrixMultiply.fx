#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform float4x4 PositionMatrix, VelocityMatrix;

uniform float  TimeDivisor;
uniform float  Strength;
uniform int    AreaType;
uniform float3 AreaCenter, AreaSize;
uniform float  AreaFalloff, AreaRotation;

uniform float2 CategoryFilter;

float computeWeight (float3 worldPosition) {
    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize, AreaRotation
    );
    return (1 - clamp(distance / AreaFalloff, 0, 1)) * Strength;
}

void PS_MatrixMultiply (
    ACCEPTS_VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1
) {
    float2 xy = GET_VPOS;

    float4 oldPosition, oldVelocity;
    readStatePV(
        xy, oldPosition, oldVelocity
    );

    if (!checkCategoryFilter(oldVelocity.w, CategoryFilter)) {
        newPosition = oldPosition;
        newVelocity = oldVelocity;
        return;
    }

    float timeScale = (TimeDivisor >= 0) ?
        getDeltaTime() / TimeDivisor
        : 1;
    float w = computeWeight(oldPosition) * timeScale;

    newPosition = lerp(
        oldPosition, mul3(oldPosition, PositionMatrix, 1),
        w
    );
    newVelocity = lerp(
        oldVelocity, mul3(oldVelocity, VelocityMatrix, 0),
        w
    );
}

technique MatrixMultiply {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_MatrixMultiply();
    }
}