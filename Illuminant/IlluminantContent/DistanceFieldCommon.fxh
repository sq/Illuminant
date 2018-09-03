uniform float                 MaximumEncodedDistance;

#define PI 3.14159265359

// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR

bool doesRayIntersectLine (float2 rayOrigin, float2 rayDirection, float2 a1, float2 a2, out float2 position) {
    float2 v1 = rayOrigin - a1,
        v2 = a2 - a1,
        v3 = float2(-rayDirection.y, rayDirection.x);
    float t1 = cross(float3(v2, 0), float3(v1, 0)).z / dot(v2, v3);
    float t2 = dot(v1, v3) / dot(v2, v3);
    if ((t1 >= 0) && (t2 >= 0) && (t2 <= 1)) {
        position = rayOrigin + (t1 * rayDirection);
        return true;
    } else {
        position = float2(0, 0);
        return false;
    }
}

bool doLinesIntersect (float2 a1, float2 a2, float2 b1, float2 b2, out float distanceAlongA) {
    distanceAlongA = 0.0;

    float2 lengthA = a2 - a1, lengthB = b2 - b1;
    float2 delta = a1 - b1;
    float q = delta.y * lengthB.x - delta.x * lengthB.y;
    float d = lengthA.x * lengthB.y - lengthA.y * lengthB.x;

    if (d == 0)
        return false;

    d = 1.0 / d;
    float r = q * d;

    if (r < 0 || r > 1)
        return false;

    float q2 = delta.y * lengthA.x - delta.x * lengthA.y;
    float s = q2 * d;

    if (s < 0 || s > 1)
        return false;

    distanceAlongA = r;
    return true;
}

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
    float2 halfDistanceTexel, distanceSliceSizeMinusHalfTexel;
};

DistanceFieldConstants makeDistanceFieldConstants() {
    float2 halfTexel = getDistanceTexelSize() * 0.5;
    DistanceFieldConstants result = {
        DistanceField.TextureSliceCount.z,
        (1.0 / DistanceField.TextureSliceCount.x) * (1.0 / 3.0),
        (1.0 / DistanceField.Extent.z) * DistanceField.TextureSliceCount.w,
        halfTexel, getDistanceSliceSize() - halfTexel
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
    float3 clampedPosition = clamp(position, 0, extent);
    float distanceToVolume = length(clampedPosition - position);

    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    // linear [0-1] -> [0-NumZSlices)
    float slicePosition = min(clampedPosition.z, vars.maximumValidZ) * vars.zToSliceIndex;
    float virtualSliceIndex = floor(slicePosition);

    float2 texelUv = clampedPosition.xy * getDistanceTexelSize();
    if (0)
        texelUv = clamp(texelUv, vars.halfDistanceTexel, vars.distanceSliceSizeMinusHalfTexel);
    
    float4 uv = float4(
        computeDistanceFieldSliceUv(virtualSliceIndex, vars.invSliceCountXTimesOneThird) + texelUv,
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