#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform float3 Add, Multiply;

uniform int    AreaType;
uniform float3 AreaCenter, AreaSize;
uniform float  AreaFalloff;

float computeWeight (float3 worldPosition) {
    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize
    );
    return 1 - clamp(distance / AreaFalloff, 0, 1);
}

void PS_PositionFMA (
    in  float2 xy            : POSITION1,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition;
    readState(
        xy, oldPosition, newVelocity, newAttributes
    );

    newPosition = lerp(
        oldPosition, float4((oldPosition.xyz * Multiply) + Add, oldPosition.w),
        computeWeight(oldPosition)
    );
}

void PS_VelocityFMA(
    in  float2 xy            : POSITION1,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldVelocity;
    readState(
        xy, newPosition, oldVelocity, newAttributes
    );

    newVelocity = lerp(
        oldVelocity, float4((oldVelocity.xyz * Multiply) + Add, oldVelocity.w),
        computeWeight(newPosition)
    );
}

technique PositionFMA {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_PositionFMA();
    }
}

technique VelocityFMA {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_VelocityFMA();
    }
}