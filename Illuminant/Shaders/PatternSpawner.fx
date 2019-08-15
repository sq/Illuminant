#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"
#include "SpawnerCommon.fxh"

uniform float4 StepWidthAndSizeScale;
uniform float4 YOffsetsAndCoordScale;
uniform float4 TexelOffsetAndMipBias;
uniform float2 CenteringOffset;

Texture2D PatternTexture;
sampler PatternSampler {
    Texture = (PatternTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

void PS_SpawnPattern (
    ACCEPTS_VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float2 xy = GET_VPOS;
    float index = floor(xy.x) + (floor(xy.y) * ChunkSizeAndIndices.x);

    PREFER_BRANCH
    if ((index < ChunkSizeAndIndices.y) || (index > ChunkSizeAndIndices.z)) {
        discard;
        return;
    }

    float relativeIndex = index - ChunkSizeAndIndices.y;

    float2 indexXy = float2(
        floor(relativeIndex % StepWidthAndSizeScale.y), floor(relativeIndex / StepWidthAndSizeScale.y)
    );
    indexXy.y += YOffsetsAndCoordScale.x;
    float2 texCoordXy = (indexXy * StepWidthAndSizeScale.zw) + TexelOffsetAndMipBias.xy;
    texCoordXy.y += YOffsetsAndCoordScale.y;
    float2 positionXy = indexXy * YOffsetsAndCoordScale.zw + CenteringOffset;

    float4 patternColor = tex2Dlod(
        PatternSampler, float4(texCoordXy, 0, TexelOffsetAndMipBias.w)
    );

    float4 random1, random2, random3;
    evaluateRandomForIndex(index, random1, random2, random3);

    float4 positionConstant = InlinePositionConstants[0];
    float4 tempPosition = evaluateFormula(0, positionConstant, Configuration[0], Configuration[1], random1, FormulaTypes.x);

    tempPosition.xy += positionXy;

    float4 attributeConstant = patternColor;
    if (MultiplyAttributeConstant)
        attributeConstant *= Configuration[5];
    else
        attributeConstant += Configuration[5];

    newPosition = mul(float4(tempPosition.xyz, 1), PositionMatrix);
    newPosition.w = tempPosition.w;

    float4 velocityConstant = Configuration[2];
    newVelocity = evaluateFormula(newPosition, velocityConstant, Configuration[3], Configuration[4], random2, FormulaTypes.y);

    newAttributes = evaluateFormula(newPosition, attributeConstant, Configuration[6], Configuration[7], random3, FormulaTypes.z);

    if (newAttributes.w < AttributeDiscardThreshold)
        discard;
}

technique SpawnPatternParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_SpawnPattern();
    }
}
