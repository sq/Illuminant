#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform float4x4 PositionMatrix, VelocityMatrix, AttributeMatrix;

uniform float  TimeDivisor;
uniform float  Strength;
uniform int    AreaType;
uniform float3 AreaCenter, AreaSize;
uniform float  AreaFalloff;

float computeWeight (float3 worldPosition) {
    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize
    );
    return (1 - clamp(distance / AreaFalloff, 0, 1)) * Strength;
}

void PS_MatrixMultiply (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition, oldVelocity, oldAttributes;
    readState(
        xy, oldPosition, oldVelocity, oldAttributes
    );

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
    newAttributes = lerp(
        oldAttributes, mul(oldAttributes, AttributeMatrix),
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