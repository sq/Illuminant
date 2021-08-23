// In any mode other than /Od, evaluateFormula is completely broken and it seems like 'randomness' is sometimes zero too
//  thanks fxc
#pragma fxcparams(/Od /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"
#include "SpawnerCommon.fxh"

void PS_Spawn (
    ACCEPTS_VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    int index1, index2;
    float positionIndexT;
    float4 random1, random2, random3, positionConstant;

    float2 xy = GET_VPOS;

    if (!Spawn_Stage1(xy, random1, random2, random3, index1, index2, positionIndexT))
        return;

    float4 position1 = InlinePositionConstants[index1],
        position2 = InlinePositionConstants[index2];
    positionConstant = lerp(position1, position2, positionIndexT);

    Spawn_Stage2(positionConstant, position2 - position1, random1, random2, random3, newPosition, newVelocity, newAttributes);
}

void PS_SpawnFromPositionTexture (
    ACCEPTS_VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    int index1, index2;
    float positionIndexT;
    float4 random1, random2, random3, positionConstant;

    float2 xy = GET_VPOS;

    if (!Spawn_Stage1(xy, random1, random2, random3, index1, index2, positionIndexT))
        return;

    float4 position1 = tex2Dlod(PositionConstantSampler, float4(index1 * PositionConstantTexel.x, 0, 0, 0)),
        position2 = tex2Dlod(PositionConstantSampler, float4(index2 * PositionConstantTexel.x, 0, 0, 0));
    positionConstant = lerp(position1, position2, positionIndexT);

    Spawn_Stage2(positionConstant, position2 - position1, random1, random2, random3, newPosition, newVelocity, newAttributes);
}

void PS_SpawnFeedback (
    ACCEPTS_VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float2 xy = GET_VPOS;
    float index = (xy.x) + (xy.y * ChunkSizeAndIndices.x);

    PREFER_BRANCH
    if ((index < ChunkSizeAndIndices.y) || (index > ChunkSizeAndIndices.z)) {
        discard;
        return;
    }

    float sourceIndex = ((index - ChunkSizeAndIndices.y) / InstanceMultiplier) + FeedbackSourceIndex;
    float sourceY, sourceX = modf(sourceIndex / SourceChunkSizeAndTexel.x, sourceY) * SourceChunkSizeAndTexel.x;
    float2 sourceXy = float2(sourceX, sourceY);

    float4 sourcePosition, sourceVelocity, sourceAttributes;
    float4 sourceUv = float4(sourceXy * SourceChunkSizeAndTexel.yz, 0, 0);

    readStateUv(sourceUv, sourcePosition, sourceVelocity, sourceAttributes);
    if ((sourcePosition.w <= SourceLifeRange.x) || (sourcePosition.w >= SourceLifeRange.y)) {
        discard;
        return;
    }

    float4 random1, random2, random3;
    evaluateRandomForIndex(index, random1, random2, random3);

    float relativeIndex = (index - ChunkSizeAndIndices.y) + ChunkSizeAndIndices.w;
    float4 positionConstant = InlinePositionConstants[0];
    if (AlignPositionConstant)
        positionConstant.xyz += sourcePosition.xyz;
    float4 tempPosition = evaluateFormula(0, positionConstant, Configuration[0], Configuration[1], random1, FormulaTypes.x);

    float4 attributeConstant = Configuration[5];
    if (MultiplyAttributeConstant)
        attributeConstant *= sourceAttributes;

    newPosition = mul(float4(tempPosition.xyz, 1), PositionMatrix);
    newPosition.w = tempPosition.w;

    if (MultiplyLife)
        newPosition.w *= sourcePosition.w;

    float4 velocityConstant = Configuration[2];
    float4 tempVelocity = evaluateFormula(tempPosition, velocityConstant, Configuration[3], Configuration[4], random2, FormulaTypes.y);
    tempVelocity += sourceVelocity * SourceVelocityFactor;

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

technique SpawnParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_Spawn();
    }
}

technique SpawnParticlesFromPositionTexture {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_SpawnFromPositionTexture();
    }
}

technique SpawnFeedbackParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_SpawnFeedback();
    }
}
