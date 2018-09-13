#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform float4x4 PositionMatrix, VelocityMatrix, AttributeMatrix;

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

// Because w is used to store unrelated data, we split it out and store it
//  and then restore it after doing a matrix multiply.
// We take a w-value to attach to the position/velocity so that it is properly
//  handled as a position or vector.
float4 mul3 (float4 oldValue, float4x4 mat, float w) {
    float4 temp = mul(float4(oldValue.xyz, w), mat);
    float3 divided;
    // FIXME: Is this right?
    if (w != 0)
        divided = temp.xyz / temp.w;
    else
        divided = temp.xyz;
    return float4(divided, oldValue.w);
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

    newPosition = lerp(
        oldPosition, mul3(oldPosition, PositionMatrix, 1),
        computeWeight(oldPosition)
    );
    newVelocity = lerp(
        oldVelocity, mul3(oldVelocity, VelocityMatrix, 0),
        computeWeight(oldPosition)
    );
    newAttributes = lerp(
        oldAttributes, mul(oldAttributes, AttributeMatrix),
        computeWeight(oldPosition)
    );
}

technique MatrixMultiply {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_MatrixMultiply();
    }
}