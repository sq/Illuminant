#define PI 3.14159265358979323846

#ifndef DISTANCE_FIELD_DEFINED
#define DISTANCE_FIELD_DEFINED

// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR

float cross2d (float2 a, float2 b) {
    return a.x*b.y - a.y*b.x;
}

bool doesRayIntersectLine (float2 rayOrigin, float2 rayDirection, float2 a1, float2 a2, out float2 position) {
    float2 v1 = rayOrigin - a1,
        v2 = a2 - a1,
        v3 = float2(-rayDirection.y, rayDirection.x);
    float t1, t2;
    float divisor = dot(v2, v3);
    if (divisor == 0) {
        t1 = t2 = 0;
    } else {
        t1 = cross2d(v2, v1) / divisor;
        t2 = dot(v1, v3) / divisor;
    }
    if ((t1 >= 0) && (t2 >= 0) && (t2 <= 1)) {
        position = rayOrigin + (t1 * rayDirection);
        return true;
    } else {
        position = float2(0, 0);
        return false;
    }
}

/*
bool doesRightRayIntersectLine (float2 rayOrigin, float2 a1, float2 a2) {
    float2 v1 = rayOrigin - a1,
        v2 = a2 - a1,
        v3 = float2(0, 1);
    float divisor = dot(v2, v3);
    float t1, t2;
    if (divisor == 0) {
        t1 = t2 = 0;
    } else {
        t1 = cross2d(v2, v1) / divisor;
        t2 = dot(v1, v3) / divisor;
    }
    return (t1 >= 0) && (t2 >= 0) && (t2 <= 1);
}
*/

#define intersectEpsilon 0.25
#define intersectOffset 0.5

// If a ray crosses an edge endpoint the ray data becomes garbage.
// We offset the ray position in this case to attempt to calculate mostly-correct data for 
//  the edge instead, and if that fails we flag the entire ray as bad.
// In most cases either the down or right ray will survive, but for pixels that are close
//  to an edge endpoint both rays may fail. Offsetting increases the odds that we get usable
//  data for that pixel.

bool isCloseX (float2 p, float2 a1, float2 a2) {
    return (abs(a1.x - p.x) < intersectEpsilon) || (abs(a2.x - p.x) < intersectEpsilon);
}

bool isCloseY (float2 p, float2 a1, float2 a2) {
    return (abs(a1.y - p.y) < intersectEpsilon) || (abs(a2.y - p.y) < intersectEpsilon);
}

#define QUANTIZE_PRECISION 100

float quantize (float v) {
    return v;
    // return floor(v * QUANTIZE_PRECISION);
}

float2 quantize2 (float2 v) {
    return v;
    // return floor(v * QUANTIZE_PRECISION);
}

bool doesDownRayIntersectLine (float2 p, float2 a1, float2 a2, inout bool bad) {
    float divisor = (a1.x-a2.x);
    if (isCloseX(p, a1, a2))
        p.x += intersectOffset;
    if (isCloseX(p, a1, a2))
        bad = true;
    bool crossesX = ((a2.x>p.x) != (a1.x>p.x));
    bool result = !bad && crossesX &&
        (p.y < (a1.y-a2.y) * (p.x-a2.x) / divisor + a2.y);
    return result && (divisor != 0);
}

bool doesRightRayIntersectLine (float2 p, float2 a1, float2 a2, inout bool bad) {
    float divisor = (a1.y-a2.y);
    if (isCloseY(p, a1, a2))
        p.y += intersectOffset;
    if (isCloseY(p, a1, a2))
        bad = true;
    bool crossesY = ((a2.y>p.y) != (a1.y>p.y));
    bool result = !bad && crossesY &&
        (p.x < (a1.x-a2.x) * (p.y-a2.y) / divisor + a2.x);
    return result && (divisor != 0);
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

float2 closestPointOnLine2 (float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    t = dot(pt - a, ab) / dot(ab, ab);
    return a + t * ab;
}

float2 closestPointOnLineSegment2 (float2 a, float2 b, float2 pt, out float t) {
    float2  ab = b - a;
    t = saturate(dot(pt - a, ab) / dot(ab, ab));
    return a + t * ab;
}

float3 closestPointOnLine3 (float3 a, float3 b, float3 pt, out float t) {
    float3  ab = b - a;
    t = dot(pt - a, ab) / dot(ab, ab);
    return a + t * ab;
}

float3 closestPointOnLineSegment3 (float3 a, float3 b, float3 pt, out float t) {
    float3  ab = b - a;
    t = saturate(dot(pt - a, ab) / dot(ab, ab));
    return a + t * ab;
}

float distanceSquaredToEdge (float2 pt, float2 a, float2 b) {
    float t;
    float2 chosen = closestPointOnLineSegment2(a, b, pt, t);
    float2 distanceSq = (pt - chosen);
    distanceSq *= distanceSq;
    return distanceSq.x + distanceSq.y;
}

/*
float _dist2 (float2 a, float2 b) {
    float2 d = (a - b);
    d *= d;
    return d.x + d.y;
}

float distanceSquaredToEdge (
    float2 p, float2 v, float2 w
) {
    float l2 = _dist2(v, w);
    if (l2 == 0) 
        return _dist2(p, v);
    float2 wv = w - v;
    float t = ((p.x - v.x) * wv.x + (p.y - v.y) * wv.y) / l2;
    t = saturate(t);
    return _dist2(
        p, 
        float2(v.x + t * wv.x, v.y + t * wv.y)
    );
}
*/


struct DistanceFieldSettings {
    // MaxConeRadius, ConeGrowthFactor, OcclusionToOpacityPower, InvScaleFactor
    float4 _ConeAndMisc;
    float4 _TextureSliceAndTexelSize;
    // StepLimit, MinimumLength, LongStepFactor
    float4 _StepAndMisc2;
    float4 TextureSliceCount;
    float4 Extent;
};

uniform DistanceFieldSettings DistanceField;

float getMaximumEncodedDistance () {
    return DistanceField.Extent.w;
}

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


float encodeDistance (float distance) {
    return DISTANCE_ZERO - (distance / getMaximumEncodedDistance());
}

float decodeDistance (float encodedDistance) {
    return (DISTANCE_ZERO - encodedDistance) * getMaximumEncodedDistance();
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
    float3 data;
    float2 halfDistanceTexel, distanceSliceSizeMinusHalfTexel;
};

DistanceFieldConstants makeDistanceFieldConstants() {
    float2 halfTexel = getDistanceTexelSize() * 0.5;
    DistanceFieldConstants result = {
        float3(
            DistanceField.TextureSliceCount.z, 
            (1.0 / max(0.001, DistanceField.TextureSliceCount.x)) * (1.0 / 3.0), 
            (1.0 / max(0.001, DistanceField.Extent.z)) * DistanceField.TextureSliceCount.w
        ),
        halfTexel, getDistanceSliceSize() - halfTexel
    };

    return result;
}

float getMaximumValidZ (in DistanceFieldConstants vars) {
    return vars.data.x;
}

float getInvSliceCountXTimesOneThird (in DistanceFieldConstants vars) {
    return vars.data.y;
}

float getZToSliceIndex (in DistanceFieldConstants vars) {
    return vars.data.z;
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
    float3 extent = DistanceField.Extent.xyz;
    float3 clampedPosition = clamp3(position, 0, extent);
    float distanceToVolume = length(clampedPosition - position);

    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    // linear [0-1] -> [0-NumZSlices)
    float slicePosition = min(clampedPosition.z, getMaximumValidZ(vars)) * getZToSliceIndex(vars);
    float virtualSliceIndex = floor(slicePosition);

    float2 texelUv = clampedPosition.xy * getDistanceTexelSize();
    if (0)
        texelUv = clamp2(texelUv, vars.halfDistanceTexel, vars.distanceSliceSizeMinusHalfTexel);
    
    float4 uv = float4(
        computeDistanceFieldSliceUv(virtualSliceIndex, getInvSliceCountXTimesOneThird(vars)) + texelUv,
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
        return getMaximumEncodedDistance();

    return sampleDistanceFieldEx(position, vars);
}

#endif