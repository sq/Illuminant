#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

struct Formula {
    float4 Constant;
    float4 RandomOffset;
    float4 RandomScale;
    // ?????????????
    float4 Padding;
};

struct _Configuration {
    float4 ChunkSizeAndIndices;
    Formula Position, Velocity, Attributes;
};

uniform _Configuration Configuration;

void VS_Spawn (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0
) {
    result = float4(xy.x, xy.y, 0, 1);
}

float4 evaluateFormula (Formula formula, float2 xy) {
    float4 result = formula.Constant;
    result += (formula.RandomScale * (random(xy) + formula.RandomOffset));
    return result;
}

void PS_Spawn (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float index = (xy.x) + (xy.y * Configuration.ChunkSizeAndIndices.x);

    [branch]
    if ((index < Configuration.ChunkSizeAndIndices.z) || (index > Configuration.ChunkSizeAndIndices.w)) {
        readState(
            xy * Texel, newPosition, newVelocity, newAttributes
        );
    } else {
        newPosition   = evaluateFormula(Configuration.Position, float2(index, 0));
        newVelocity   = evaluateFormula(Configuration.Velocity, float2(index, 1));
        newAttributes = evaluateFormula(Configuration.Attributes, float2(index, 2));
    }
}

technique SpawnParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_Spawn();
    }
}
