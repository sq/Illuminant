#define MAX_INLINE_POSITION_CONSTANTS 4

uniform const float  AlignVelocityAndPosition, MultiplyLife, SpawnFromEntireWindow;
uniform const float  AlignPositionConstant, MultiplyAttributeConstant, PolygonLoop;
uniform const float  PolygonRate, SourceVelocityFactor, FeedbackSourceIndex, AttributeDiscardThreshold, InstanceMultiplier;
uniform const float4 ChunkSizeAndIndices;
uniform const float4 Configuration[9];
uniform const float4 FormulaTypes;
uniform const float4x4 PositionMatrix, VelocityMatrix;
uniform const float3 SourceChunkSizeAndTexel, AxisMask;
uniform const float2 SourceLifeRange;

uniform const float  PositionConstantCount;
uniform const float2 PositionConstantTexel;
uniform const float4 InlinePositionConstants[MAX_INLINE_POSITION_CONSTANTS];

Texture2D PositionConstantTexture;
sampler PositionConstantSampler {
    Texture = (PositionConstantTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
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

float3 generateRandomNormal2 (float randomness) {
    float phi = randomness.x * PI * 2;

    return float3(
        sin(phi), cos(phi), 0
    );
}

float3 generateRandomNormal3 (float2 randomness) {
    float phi = randomness.x * PI * 2;
    float costheta = (randomness.y - 0.5) * 2;
    float theta = acos(costheta);

    return float3(
        sin(theta) * cos(phi),
        sin(theta) * sin(phi),
        cos(theta)
    );
}

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
            float3 randomNormal;
            randomNormal = normalize(generateRandomNormal3(randomness.xy) * AxisMask);

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

    float4 tempVelocity = evaluateFormula(tempPosition, Configuration[2], Configuration[3], Configuration[4], random2, FormulaTypes.y);
    newAttributes = evaluateFormula(0, Configuration[5], Configuration[6], Configuration[7], random3, FormulaTypes.z);
    
    float towardsDistance = length(towardsNext);
    if (towardsDistance > 0.0001) {
        // FIXME: float->float4, random3.w
        float towardsSpeed = evaluateFormula(0, Configuration[8].x, Configuration[8].y, Configuration[8].z, random3.w, FormulaTypes.w).x;
        tempVelocity += towardsSpeed * (towardsNext / towardsDistance);
    }

    newVelocity = mul(float4(tempVelocity.xyz, 1), VelocityMatrix);
    newVelocity.w = tempVelocity.w;

#if FNA
    // HACK: Some garbage math semantics in GLSL mean particles with 0 velocity become invisible
    if (length(newVelocity.xyz) < 1)
        newVelocity.z += 0.01;
#endif

    if (newAttributes.w < AttributeDiscardThreshold)
        discard;
}