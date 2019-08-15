// In any mode other than /Od, evaluateFormula is completely broken and it seems like 'randomness' is sometimes zero too
//  thanks fxc
#pragma fxcparams(/Od /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "ParticleCommon.fxh"
#include "RandomCommon.fxh"

#define MAX_INLINE_POSITION_CONSTANTS 4

uniform float  AlignVelocityAndPosition, ZeroZAxis, MultiplyLife, SpawnFromEntireWindow;
uniform float  AlignPositionConstant, MultiplyAttributeConstant, PolygonLoop;
uniform float  PolygonRate, SourceVelocityFactor, FeedbackSourceIndex, AttributeDiscardThreshold, InstanceMultiplier;
uniform float4 ChunkSizeAndIndices;
uniform float4 Configuration[9];
uniform float4 FormulaTypes;
uniform float4x4 PositionMatrix;
uniform float3 SourceChunkSizeAndTexel;

uniform float  PositionConstantCount;
uniform float2 PositionConstantTexel;
uniform float4 InlinePositionConstants[MAX_INLINE_POSITION_CONSTANTS];

uniform float4 StepWidthAndSizeScale;
uniform float4 InitialOffsetAndCoord;
uniform float4 ModulusesAndMipBias;

Texture2D PositionConstantTexture;
sampler PositionConstantSampler {
    Texture = (PositionConstantTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

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

#define FormulaType_Linear 0
#define FormulaType_Spherical 1
#define FormulaType_Towards 2
#define FormulaType_Rectangular 3

float4 evaluateFormula (float4 origin, float4 constant, float4 scale, float4 offset, float4 randomness, float type) {
    float4 nonCircular = (randomness + offset) * scale;
    float4 type0 = constant + nonCircular;

    uint itype = (uint)abs(floor(type));
    switch (itype) {
        case FormulaType_Linear:
        default: {
            return type0;
        }
        case FormulaType_Rectangular:
        case FormulaType_Spherical: {
            float o = randomness.x * PI * 2;
            float z = (randomness.y - 0.5) * 2;
            if (ZeroZAxis)
                z = 0;
            float xyMultiplier = sqrt(1 - (z * z));
            float3 randomNormal = float3(xyMultiplier * cos(o), xyMultiplier * sin(o), z);
            float3 circular = float3(
                randomNormal.x * randomness.z * scale.x,
                randomNormal.y * randomness.z * scale.y,
                randomNormal.z * randomness.z * scale.z
            );
            float3 result;
            if (itype == FormulaType_Rectangular) {
                const float sqrt2 = 1.41421356237;
                float3 edge = abs(offset.xyz);
                result = clamp(offset.xyz * randomNormal * sqrt2, -edge, edge);
                result += constant.xyz + circular.xyz;
            } else {
                circular += randomNormal * offset.xyz;
                result = constant.xyz + circular.xyz;
            }
            return float4(result, type0.w);
        }
        case FormulaType_Towards: {
            float3 distance = constant - origin;
            float ldistance = length(distance);
            if (ldistance < 0.1)
                return float4(0, 0, 0, constant.w);
            float3 direction = distance / ldistance;
            float3 randomSpeed = (randomness.x * scale.xyz * direction.xyz);
            float3 fixedSpeed = (offset.xyz * direction);
            return float4(randomSpeed + fixedSpeed, type0.w);
        }
    }

    return type0;
}

void evaluateRandomForIndex (in float index, out float4 random1, out float4 random2, out float4 random3) {
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

bool Spawn_Stage1 (
    in float2 xy,
    out float4 random1, out float4 random2, out float4 random3,
    out int index1, out int index2, out float positionIndexT
) {
    float index = (xy.x) + (xy.y * ChunkSizeAndIndices.x);

    PREFER_BRANCH
    if ((index < ChunkSizeAndIndices.y) || (index > ChunkSizeAndIndices.z)) {
        discard;
        return false;
    }

    evaluateRandomForIndex(index, random1, random2, random3);

    // Ensure the z axis of generated circular coordinates is 0, resulting in pure xy normals
    float relativeIndex = (index - ChunkSizeAndIndices.y);
    if (PolygonRate > 0.05) {
        float polyRate = PolygonRate;
        float positionIndexF = (relativeIndex / polyRate) + ChunkSizeAndIndices.w;
        float divisor = PositionConstantCount;
        float positionIndexI;
        positionIndexT = modf(positionIndexF, positionIndexI);
        if (PolygonLoop) {
            index1 = positionIndexI % divisor;
            index2 = (positionIndexI + 1) % divisor;
        } else {
            index1 = positionIndexI % divisor;
            index2 = min(index1 + 1, divisor - 1);
        }
    } else {
        index1 = index2 = (relativeIndex + ChunkSizeAndIndices.w) % PositionConstantCount;
        positionIndexT = 0;
    }

    return true;
}

void Spawn_Stage2(
    in float4 positionConstant, in float4 towardsNext,
    in float4 random1, in float4 random2, in float4 random3,
    out float4 newPosition,
    out float4 newVelocity,
    out float4 newAttributes
) {
    float4 tempPosition = evaluateFormula(0, positionConstant, Configuration[0], Configuration[1], random1, FormulaTypes.x);

    newPosition = mul(float4(tempPosition.xyz, 1), PositionMatrix);
    newPosition.w = tempPosition.w;

    newVelocity = evaluateFormula(newPosition, Configuration[2], Configuration[3], Configuration[4], random2, FormulaTypes.y);
    newAttributes = evaluateFormula(0, Configuration[5], Configuration[6], Configuration[7], random3, FormulaTypes.z);
    
    float towardsDistance = length(towardsNext);
    if (towardsDistance > 0.0001) {
        // FIXME: float->float4, random3.w
        float towardsSpeed = evaluateFormula(0, Configuration[8].x, Configuration[8].y, Configuration[8].z, random3.w, FormulaTypes.w).x;
        newVelocity += towardsSpeed * (towardsNext / towardsDistance);
    }

    if (newAttributes.w < AttributeDiscardThreshold)
        discard;
}

void PS_SpawnPattern (
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

    float relativeIndex = index - ChunkSizeAndIndices.x;

    float2 indexXy = floor(float2(
        relativeIndex % StepWidthAndSizeScale.y, relativeIndex / StepWidthAndSizeScale.y
    ));
    float2 texCoordXy = indexXy * StepWidthAndSizeScale.zw + InitialOffsetAndCoord.zw;
    float2 positionXy = floor(indexXy * StepWidthAndSizeScale.x) + InitialOffsetAndCoord.xy;

    // FIXME: Mip bias
    float4 patternColor = tex2D(PatternSampler, texCoordXy);

    float4 random1, random2, random3;
    evaluateRandomForIndex(index, random1, random2, random3);

    /*
    float4 positionConstant = InlinePositionConstants[0];
    // FIXME: Align around center
    float4 pixelAlignment = float4(0, 0, 0, 0);
    float4 tempPosition = evaluateFormula(0, positionConstant + pixelAlignment, Configuration[0], Configuration[1], random1, FormulaTypes.x);

    float4 attributeConstant = patternColor;
    if (MultiplyAttributeConstant)
        attributeConstant *= Configuration[5];
    else
        attributeConstant += Configuration[5];
    */
    float4 tempPosition = float4(positionXy, 0, 0);

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
