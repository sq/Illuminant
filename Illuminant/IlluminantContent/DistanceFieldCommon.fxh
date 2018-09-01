uniform float                 MaximumEncodedDistance;

#define PI 3.14159265359

// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

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
    return edgeStart + ((edgeEnd - edgeStart) * saturate(u));
}

float encodeDistance (float distance) {
    return DISTANCE_ZERO - (distance / MaximumEncodedDistance);
}

float decodeDistance (float encodedDistance) {
    return (DISTANCE_ZERO - encodedDistance) * MaximumEncodedDistance;
}


struct DistanceFieldSettings {
    // MaxConeRadius, ConeGrowthFactor, OcclusionToOpacityPower, InvScaleFactor
    float4 _ConeAndMisc;
    float4 _TextureSliceAndTexelSize;
    // StepLimit, MinimumLength, LongStepFactor
    float4 _StepAndMisc2;
    float4 TextureSliceCount;
    float3 Extent;
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
    float maximumValidZ;
    float invSliceCountXTimesOneThird;
    float zToSliceIndex;
};

DistanceFieldConstants makeDistanceFieldConstants() {
    DistanceFieldConstants result = {
        DistanceField.TextureSliceCount.z,
        (1.0 / DistanceField.TextureSliceCount.x) * (1.0 / 3.0),
        (1.0 / DistanceField.Extent.z) * DistanceField.TextureSliceCount.w
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
    return float2(columnIndex, rowIndex) * getDistanceSliceSize();
}

float sampleDistanceFieldEx (
    float3 position, 
    DistanceFieldConstants vars
) {
    float3 extent = DistanceField.Extent;
    extent.z = vars.maximumValidZ;
    float3 clampedPosition = clamp(position, 0, extent);
    float distanceToVolume = length(clampedPosition - position);

    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    // linear [0-1] -> [0-NumZSlices)
    float slicePosition = clampedPosition.z * vars.zToSliceIndex;
    float virtualSliceIndex = floor(slicePosition);
    
    float4 uv = float4(
        computeDistanceFieldSliceUv(virtualSliceIndex, vars.invSliceCountXTimesOneThird) +
            (clampedPosition.xy * getDistanceTexelSize()),
        0, 0
    );

    float4 packedSample = tex2Dlod(DistanceFieldTextureSampler, uv);

    float maskPatternIndex = fmod(virtualSliceIndex, 3);
    float subslice = slicePosition - virtualSliceIndex;

    float2 samples =
        (maskPatternIndex >= 2)
            ? packedSample.ba
            : (maskPatternIndex >= 1)
                ? packedSample.gb
                : packedSample.rg;

    float blendedSample = lerp(
        samples.x, samples.y, subslice
    );

    float decodedDistance = decodeDistance(blendedSample);

    // HACK: Samples outside the distance field will be wrong if they just
    //  read the closest distance in the field.
    return decodedDistance + distanceToVolume;
}

float sampleDistanceField (
    float3 position,
    DistanceFieldConstants vars
) {
    // This generates a shader branch :(
    if (DistanceField.Extent.x <= 0)
        return MaximumEncodedDistance;

    return sampleDistanceFieldEx(position, vars);
}