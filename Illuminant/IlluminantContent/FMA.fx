#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform float4 PositionAdd, PositionMultiply;
uniform float4 VelocityAdd, VelocityMultiply;
uniform float4 AttributeAdd, AttributeMultiply;

uniform float  Strength;
uniform int    AreaType;
uniform float3 AreaCenter, AreaSize;
uniform float  AreaFalloff;

float computeWeight (float3 worldPosition) {
    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize
    );
    return (1 - saturate(distance / AreaFalloff)) * Strength;
}

float4 computeFMA (
    float4 oldPosition, float4 oldValue, float4 multiply, float4 add
) {
    float weight = computeWeight(oldPosition);
    return lerp(
        oldValue, (oldValue * multiply) + add, weight
    );
}

void PS_FMA (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition, oldVelocity, oldAttributes;
    readState(
        xy * System.Texel, oldPosition, oldVelocity, oldAttributes
    );

    newPosition   = computeFMA(oldPosition, oldPosition, PositionMultiply, PositionAdd);
    newVelocity   = computeFMA(oldPosition, oldVelocity, VelocityMultiply, VelocityAdd);
    newAttributes = computeFMA(oldPosition, oldAttributes, AttributeMultiply, AttributeAdd);
}

technique FMA {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_FMA();
    }
}