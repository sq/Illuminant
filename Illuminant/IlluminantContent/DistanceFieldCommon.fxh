uniform float                 MaximumEncodedDistance;

#define PI 3.14159265359

// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

// Maximum distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_MAX MaximumEncodedDistance

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

half encodeDistance (half distance) {
    return DISTANCE_ZERO - (distance / DISTANCE_MAX);
}

half decodeDistance (half encodedDistance) {
    return (DISTANCE_ZERO - encodedDistance) * DISTANCE_MAX;
}


struct DistanceFieldSettings {
    // MaxConeRadius, ConeGrowthFactor, OcclusionToOpacityPower, InvScaleFactor
    float4 _ConeAndMisc;
    float4 _TextureSliceAndTexelSize;
    // StepLimit, MinimumLength, LongStepFactor
    float4 _StepAndMisc2;
    float3 Extent;
    float3 TextureSliceCount;
};

uniform DistanceFieldSettings DistanceField;

// FIXME: int?
float getStepLimit () {
    return DistanceField._StepAndMisc2.x;
}

float getMinStepSize () {
    return DistanceField._StepAndMisc2.y;
}

float getLongStepFactor () {
    return DistanceField._StepAndMisc2.z;
}

float getMaxConeRadius () {
    return DistanceField._ConeAndMisc.x;
}

float getConeGrowthFactor () {
    return DistanceField._ConeAndMisc.y;
}

float getOcclusionToOpacityPower () {
    return DistanceField._ConeAndMisc.z;
}

float getInvScaleFactorX () {
    return DistanceField._ConeAndMisc.w;
}

float getInvScaleFactorY () {
    return DistanceField._StepAndMisc2.w;
}

float2 getInvScaleFactors () {
    return float2(DistanceField._ConeAndMisc.w, DistanceField._StepAndMisc2.w);
}

float2 getDistanceSliceSize () {
    return DistanceField._TextureSliceAndTexelSize.xy;
}

float2 getDistanceTexelSize () {
    return DistanceField._TextureSliceAndTexelSize.zw;
}


Texture2D DistanceFieldTexture;
sampler   DistanceFieldTextureSampler {
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

half2 computeDistanceFieldSliceUv (
    half virtualSliceIndex,
    half invSliceCountXTimesOneThird
) {
    half rowIndexF   = virtualSliceIndex * invSliceCountXTimesOneThird;
    half rowIndex    = floor(rowIndexF);
    half columnIndex = floor((rowIndexF - rowIndex) * DistanceField.TextureSliceCount.x);
    return half2(columnIndex, rowIndex) * getDistanceSliceSize();
}

half sampleDistanceField (
    half3 position, 
    DistanceFieldConstants vars
) {
    if (DistanceField.Extent.x <= 0)
        return DISTANCE_MAX;

    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    // linear [0-1] -> [0-NumZSlices)
    half slicePosition = clamp(position.z * vars.zToSliceIndex, 0, vars.sliceCountZMinus1);
    half virtualSliceIndex = floor(slicePosition);
    
    half3 offsetPosition = position + float3(1, 1, 0);
    half3 clampedPosition = clamp(offsetPosition, 1, DistanceField.Extent - 1);
    half distanceToVolume = length(clampedPosition - offsetPosition);

    half4 uv = half4(
        computeDistanceFieldSliceUv(virtualSliceIndex, vars.invSliceCountXTimesOneThird) +
            (clampedPosition.xy * getDistanceTexelSize()),
        0, 0
    );

    half4 packedSample = tex2Dlod(DistanceFieldTextureSampler, uv);

    half maskPatternIndex = virtualSliceIndex % 3;
    half subslice = slicePosition - virtualSliceIndex;

    half2 samples;
    if (maskPatternIndex >= 2)
        samples = packedSample.ba;
    else if (maskPatternIndex >= 1)
        samples = packedSample.gb;
    else
        samples = packedSample.rg;

    half blendedSample = lerp(
        samples.x, samples.y, subslice
    );

    half decodedDistance = decodeDistance(blendedSample);

    // HACK: Samples outside the distance field will be wrong if they just
    //  read the closest distance in the field.
    return decodedDistance + distanceToVolume;
}