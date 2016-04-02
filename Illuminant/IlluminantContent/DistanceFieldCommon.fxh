#define PI 3.14159265359

// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

// Maximum distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_MAX 512

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR


float closestPointOnEdgeAsFactor (
    float2 pt, float2 edgeStart, float2 edgeEnd
) {
    float2 edgeDelta = edgeEnd - edgeStart;
    float  edgeLength = length(edgeDelta);
    edgeLength *= edgeLength;

    float2 pointDelta = (pt - edgeStart) * edgeDelta;
    return (pointDelta.x + pointDelta.y) / edgeLength;
}

float2 closestPointOnEdge (
    float2 pt, float2 edgeStart, float2 edgeEnd
) {
    float u = closestPointOnEdgeAsFactor(pt, edgeStart, edgeEnd);
    return edgeStart + ((edgeEnd - edgeStart) * clamp(u, 0, 1));
}

float4 encodeDistance (float distance) {
    return DISTANCE_ZERO - (distance / DISTANCE_MAX);
}

float decodeDistance (float encodedDistance) {
    return (DISTANCE_ZERO - encodedDistance) * DISTANCE_MAX;
}

struct DistanceFieldSettings {
    // StepLimit, MinimumLength, LongStepFactor
    float3 Step;

    float  MaxConeRadius;
    float  ConeGrowthFactor;
    float  OcclusionToOpacityPower;
    float  InvScaleFactor;
    float3 Extent;
    float3 TextureSliceCount;
    float2 TextureSliceSize;
    float2 TextureTexelSize;
};

uniform DistanceFieldSettings DistanceField;

Texture2D DistanceFieldTexture        : register(t1);
sampler   DistanceFieldTextureSampler : register(s1) {
    Texture = (DistanceFieldTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = DISTANCE_FIELD_FILTER;
    MagFilter = DISTANCE_FIELD_FILTER;
};

struct DistanceFieldConstants {
    float sliceCountZMinus1;
    float invSliceCountXTimesOneThird;
    float zToSliceIndex;
};

DistanceFieldConstants makeDistanceFieldConstants() {
    DistanceFieldConstants result = {
        DistanceField.TextureSliceCount.z - 1,
        (1.0 / DistanceField.TextureSliceCount.x) * (1.0 / 3.0),
        (1.0 / DistanceField.Extent.z) * DistanceField.TextureSliceCount.z
    };

    return result;
}

float2 computeDistanceFieldSliceUv (
    float virtualSliceIndex,
    float invSliceCountXTimesOneThird
) {
    float rowIndexF   = virtualSliceIndex * invSliceCountXTimesOneThird;
    float rowIndex    = floor(rowIndexF);
    float columnIndex = floor((rowIndexF - rowIndex) * DistanceField.TextureSliceCount.x);
    return float2(columnIndex, rowIndex) * DistanceField.TextureSliceSize.xy;
}

float sampleDistanceField (
    float3 position, 
    DistanceFieldConstants vars
) {
    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    // linear [0-1] -> [0-NumZSlices)
    float slicePosition = clamp(position.z * vars.zToSliceIndex, 0, vars.sliceCountZMinus1);
    float virtualSliceIndex = floor(slicePosition);
    
    float3 clampedPosition = clamp(position + float3(1, 1, 0), 1, DistanceField.Extent - 1);
    float distanceToVolume = length(clampedPosition - position);

    float4 uv = float4(
        computeDistanceFieldSliceUv(virtualSliceIndex, vars.invSliceCountXTimesOneThird) +
            (clampedPosition.xy * DistanceField.TextureTexelSize),
        0, 0
    );

    float4 packedSample = tex2Dlod(DistanceFieldTextureSampler, uv);

    float maskPatternIndex = virtualSliceIndex % 3;
    float subslice = slicePosition - virtualSliceIndex;

    float2 samples;
    if (maskPatternIndex >= 2)
        samples = packedSample.ba;
    else if (maskPatternIndex >= 1)
        samples = packedSample.gb;
    else
        samples = packedSample.rg;

    float blendedSample = lerp(
        samples.x, samples.y, subslice
    );

    float decodedDistance = decodeDistance(blendedSample);

    // HACK: Samples outside the distance field will be wrong if they just
    //  read the closest distance in the field.
    return decodedDistance + distanceToVolume;
}