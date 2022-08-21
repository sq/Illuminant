#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"
#include "SpawnerCommon.fxh"

uniform const float4 StepWidthAndSizeScale;
uniform const float4 YOffsetsAndCoordScale;
uniform const float4 TexelOffsetAndMipBias;
uniform const float2 CenteringOffset;

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

    float relativeIndex = floor(index - ChunkSizeAndIndices.y);
    float divisor = StepWidthAndSizeScale.x;
    float particlesPerRow = StepWidthAndSizeScale.y;

    float2 indexXy = float2(
        floor(relativeIndex % particlesPerRow), floor(relativeIndex / particlesPerRow)
    );
    indexXy.y += YOffsetsAndCoordScale.x;
    float2 texCoordXy = (indexXy * StepWidthAndSizeScale.zw) + TexelOffsetAndMipBias.xy;
    texCoordXy.y += YOffsetsAndCoordScale.y;
    float2 positionXy = indexXy * YOffsetsAndCoordScale.zw + CenteringOffset;

    // FIXME: HACK: UGH: The way the next-power-of-two compensation stuff works, we can
    //  end up generating a TON of extra particles on the right and bottom sides of the
    //  spawn rectangle. The centering offset and other stuff is still right.
    // So for now, just reject the garbage particles.
    // Incidentally this seems to also work around the bug in mojoshader's impl of tex2dlod. Yay!
    if (any(texCoordXy > 1)) {
        discard;
        return;
    }

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
    float4 tempVelocity = evaluateFormula(tempPosition, velocityConstant, Configuration[3], Configuration[4], random2, FormulaTypes.y);

    newVelocity = mul(float4(tempVelocity.xyz, 1), VelocityMatrix);
    newVelocity.w = tempVelocity.w;

#if FNA
    // HACK: Some garbage math semantics in GLSL mean particles with 0 velocity become invisible
    if (length(newVelocity.xyz) < 1)
        newVelocity.z += 0.01;
#endif

    newAttributes = evaluateFormula(tempPosition, attributeConstant, Configuration[6], Configuration[7], random3, FormulaTypes.z);

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
