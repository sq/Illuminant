#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

uniform float4 ChunkSizeAndIndices;
uniform float4 Configuration[9];

void VS_Spawn (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0
) {
    result = float4(xy.x, xy.y, 0, 1);
}

float4 evaluateFormula (float4 constant, float4 randomOffset, float4 randomScale, float2 xy) {
    float4 result = constant;
    result += (randomScale * (random(xy) + randomOffset));
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
    if ((index < ChunkSizeAndIndices.z) || (index > ChunkSizeAndIndices.w)) {
        readState(
            xy * Texel, newPosition, newVelocity, newAttributes
        );
    } else {
        newPosition   = evaluateFormula(Configuration[0], Configuration[1], Configuration[2], float2(index, 0));
        newVelocity   = evaluateFormula(Configuration[3], Configuration[4], Configuration[5], float2(index, 1));
        newAttributes = evaluateFormula(Configuration[6], Configuration[7], Configuration[8], float2(index, 2));
    }
}

technique SpawnParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_Spawn();
    }
}
