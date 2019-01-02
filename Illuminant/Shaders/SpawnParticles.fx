#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

#define MAX_POSITION_CONSTANTS 32

uniform bool   AlignVelocityAndPosition, ZeroZAxis;
uniform bool   AlignPositionConstant, MultiplyAttributeConstant;
uniform float  PolygonRate, SourceVelocityFactor, FeedbackSourceIndex, PositionConstantCount, AttributeDiscardThreshold;
uniform float4 PositionConstants[MAX_POSITION_CONSTANTS];
uniform float4 ChunkSizeAndIndices;
uniform float4 Configuration[8];
uniform float4 FormulaTypes;
uniform float4x4 PositionMatrix;
uniform float3 SourceChunkSizeAndTexel;
uniform float4 PatternSizeRowSizeAndResolution;
uniform float2 InitialPatternXY;

Texture2D PatternTexture;
sampler PatternSampler {
    Texture = (PatternTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

void VS_Spawn (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0
) {
    result = float4(xy.x, xy.y, 0, 1);
}

float4 evaluateFormula (float4 origin, float4 constant, float4 scale, float4 offset, float4 randomness, float type) {
    float4 nonCircular = (randomness + offset) * scale;

    uint itype = (uint)abs(floor(type));
    switch (itype) {
        case 1: {
            float o = randomness.x * PI * 2;
            float z = (randomness.y - 0.5) * 2;
            if (ZeroZAxis)
                z = 0;
            float xyMultiplier = sqrt(1 - (z * z));
            float3 randomNormal = float3(xyMultiplier * cos(o), xyMultiplier * sin(o), z);
            float4 circular = float4(
                randomNormal.x * randomness.z * scale.x,
                randomNormal.y * randomness.z * scale.y,
                randomNormal.z * randomness.z * scale.z,
                nonCircular.w
                );
            float4 result = constant + circular;
            result.xyz += randomNormal * offset.xyz;
            return result;
        }
        case 2: {
            float3 distance = constant - origin;
            if (length(distance) < 0.1)
                return float4(0, 0, 0, constant.w);
            float3 direction = normalize(distance);
            float3 randomSpeed = (randomness.x * scale.xyz * direction.xyz);
            float3 fixedSpeed = (offset.xyz * direction);
            return float4(randomSpeed + fixedSpeed, constant.w);
        }
    }

    return constant + nonCircular;
}

void evaluateRandomForIndex(in float index, out float4 random1, out float4 random2, out float4 random3) {
    float2 randomOffset1 = float2(index % 8039, 0 + (index % 57));
    float2 randomOffset2 = float2(index % 6180, 1 + (index % 4031));
    float2 randomOffset3 = float2(index % 2025, 2 + (index % 65531));
    random1 = random(randomOffset1);
    random2 = random(randomOffset2);
    random3 = random(randomOffset3);

    // The x and y element of random samples determines the normal
    if (AlignVelocityAndPosition)
        random2.xy = random1.xy;
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
        discard;
        return;
    }

    float4 random1, random2, random3;
    evaluateRandomForIndex(index, random1, random2, random3);

    // Ensure the z axis of generated circular coordinates is 0, resulting in pure xy normals
    float relativeIndex = (index - ChunkSizeAndIndices.y);
    float4 positionConstant;
    if (PolygonRate > 1) {
        float positionIndexF = (relativeIndex / PolygonRate) + ChunkSizeAndIndices.w;
        float positionIndexI, positionIndexT = modf(positionIndexF, positionIndexI);
        float4 position1 = PositionConstants[positionIndexI % PositionConstantCount],
            position2 = PositionConstants[(positionIndexI + 1) % PositionConstantCount];
        positionConstant = lerp(position1, position2, positionIndexT);
    } else {
        float positionIndex = (relativeIndex + ChunkSizeAndIndices.w) % PositionConstantCount;
        positionConstant = PositionConstants[positionIndex];
    }
    float4 tempPosition = evaluateFormula(0, positionConstant, Configuration[0], Configuration[1], random1, FormulaTypes.x);

    newPosition   = mul(tempPosition, PositionMatrix);
    newPosition.w = tempPosition.w;
    newVelocity   = evaluateFormula(newPosition, Configuration[2], Configuration[3], Configuration[4], random2, FormulaTypes.y);
    newAttributes = evaluateFormula(newPosition, Configuration[5], Configuration[6], Configuration[7], random3, FormulaTypes.z);

    if (newAttributes.w < AttributeDiscardThreshold)
        discard;
}

void PS_SpawnFeedback (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float index = (xy.x) + (xy.y * ChunkSizeAndIndices.x);

    [branch]
    if ((index < ChunkSizeAndIndices.y) || (index > ChunkSizeAndIndices.z)) {
        discard;
        return;
    }

    float sourceIndex = (index - ChunkSizeAndIndices.y) + FeedbackSourceIndex;
    float sourceY, sourceX = modf(sourceIndex / SourceChunkSizeAndTexel.x, sourceY) * SourceChunkSizeAndTexel.x;
    float2 sourceXy = float2(sourceX, sourceY);

    float4 sourcePosition, sourceVelocity, sourceAttributes;
    float4 sourceUv = float4(sourceXy * SourceChunkSizeAndTexel.yz, 0, 0);

    readStateUv(sourceUv, sourcePosition, sourceVelocity, sourceAttributes);

    float4 random1, random2, random3;
    evaluateRandomForIndex(index, random1, random2, random3);

    float relativeIndex = (index - ChunkSizeAndIndices.y) + ChunkSizeAndIndices.w;
    float4 positionConstant = PositionConstants[0];
    if (AlignPositionConstant)
        positionConstant += sourcePosition;
    float4 tempPosition = evaluateFormula(0, positionConstant, Configuration[0], Configuration[1], random1, FormulaTypes.x);

    float4 attributeConstant = Configuration[5];
    if (MultiplyAttributeConstant)
        attributeConstant *= sourceAttributes;

    newPosition = mul(tempPosition, PositionMatrix);
    newPosition.w = tempPosition.w;

    float4 velocityConstant = Configuration[2];
    newVelocity = evaluateFormula(newPosition, velocityConstant, Configuration[3], Configuration[4], random2, FormulaTypes.y);
    newVelocity += sourceVelocity * SourceVelocityFactor;

    newAttributes = evaluateFormula(newPosition, attributeConstant, Configuration[6], Configuration[7], random3, FormulaTypes.z);

    if (newAttributes.w < AttributeDiscardThreshold)
        discard;
}

void PS_SpawnPattern (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float index = (xy.x) + (xy.y * ChunkSizeAndIndices.x);

    [branch]
    if ((index < ChunkSizeAndIndices.y) || (index > ChunkSizeAndIndices.z)) {
        discard;
        return;
    }

    float2 patternSize = PatternSizeRowSizeAndResolution.xy;
    float  rowSizeInParticles = PatternSizeRowSizeAndResolution.z;
    float  resolution = PatternSizeRowSizeAndResolution.w;
    float  invResolution = 1.0 / resolution;

    float relativeIndex = (index - ChunkSizeAndIndices.y) + ChunkSizeAndIndices.w;
    float2 patternXy = InitialPatternXY;
    patternXy.x += (relativeIndex % rowSizeInParticles) * invResolution;
    patternXy.y += floor(relativeIndex / rowSizeInParticles) * invResolution;
    float4 patternUv = float4((patternXy - 0.5) / patternSize, 0, max(log2(invResolution), 0));
    float4 patternColor = tex2Dlod(PatternSampler, patternUv);

    float4 random1, random2, random3;
    evaluateRandomForIndex(index, random1, random2, random3);

    float4 positionConstant = PositionConstants[0];
    float4 pixelAlignment = float4(patternXy - (patternSize * 0.5), 0, 0);
    float4 tempPosition = evaluateFormula(0, positionConstant + pixelAlignment, Configuration[0], Configuration[1], random1, FormulaTypes.x);

    float4 attributeConstant = patternColor;
    if (MultiplyAttributeConstant)
        attributeConstant *= Configuration[5];
    else
        attributeConstant += Configuration[5];

    newPosition = mul(tempPosition, PositionMatrix);
    newPosition.w = tempPosition.w;

    float4 velocityConstant = Configuration[2];
    newVelocity = evaluateFormula(newPosition, velocityConstant, Configuration[3], Configuration[4], random2, FormulaTypes.y);

    newAttributes = evaluateFormula(newPosition, attributeConstant, Configuration[6], Configuration[7], random3, FormulaTypes.z);

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

technique SpawnFeedbackParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_SpawnFeedback();
    }
}

technique SpawnPatternParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Spawn();
        pixelShader = compile ps_3_0 PS_SpawnPattern();
    }
}
