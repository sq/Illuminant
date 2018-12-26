#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

#define PI 3.14159265358979323846
#define MAX_POSITION_CONSTANTS 32

uniform float  PositionConstantCount;
uniform float4 PositionConstants[MAX_POSITION_CONSTANTS];
uniform float4 ChunkSizeAndIndices;
uniform float4 Configuration[8];
uniform float  RandomCircularity[3];
uniform float4x4 PositionMatrix;

void VS_Spawn (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0
) {
    result = float4(xy.x, xy.y, 0, 1);
}

float4 evaluateFormula (float4 constant, float4 scale, float4 offset, float randomCircularity, float2 xy) {
    float4 result = constant;

    float4 rs = random(xy);
    float4 nonCircular = (rs + offset) * scale;

    if (randomCircularity >= 0.5) {
        float o = rs.x * PI * 2;
        float z = (rs.y - 0.5) * 2;
        float multiplier = sqrt(1 - (z * z));
        float3 randomNormal = float3(multiplier * cos(o), multiplier * sin(o), z);
        float4 circular = float4(
            randomNormal.x * rs.z * scale.x,
            randomNormal.y * rs.z * scale.y,
            randomNormal.z * rs.z * scale.z,
            nonCircular.w
        );
        result += circular;
        result.xyz += randomNormal * offset.xyz;
    } else {
        result += nonCircular;
    }

    return result;
}

void PS_Spawn (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float index = (xy.x) + (xy.y * ChunkSizeAndIndices.x);

    [branch]
    if ((index < ChunkSizeAndIndices.y) || (index > ChunkSizeAndIndices.z)) {
        readStateOrDiscard(
            xy, newPosition, newVelocity, newAttributes
        );
    } else {
        float relativeIndex = (index - ChunkSizeAndIndices.y) + ChunkSizeAndIndices.w;
        float positionIndex = relativeIndex % PositionConstantCount;
        float4 positionConstant = PositionConstants[positionIndex];
        newPosition   = evaluateFormula(positionConstant, Configuration[0], Configuration[1], RandomCircularity[0], float2(index % 8039, 0 + (index % 57)));
        newPosition   = mul3(newPosition, PositionMatrix, 1);
        newVelocity   = evaluateFormula(Configuration[2], Configuration[3], Configuration[4], RandomCircularity[1], float2(index % 6180, 1 + (index % 4031)));
        newAttributes = evaluateFormula(Configuration[5], Configuration[6], Configuration[7], RandomCircularity[2], float2(index % 2025, 2 + (index % 65531)));
    }
}

technique SpawnParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_Spawn();
    }
}
