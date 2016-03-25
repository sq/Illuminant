// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

// Maximum distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_MAX 256

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR

// HACK: Step by a fraction of the distance to the next object for better accuracy
// 0.5 seems to eliminate banding artifacts and stuff.
#define PARTIAL_STEP_SIZE 0.5

// The minimum and maximum approximate cone tracing radius
// The radius increases as the cone approaches the light source
// Raising the maximum produces soft shadows, but if it's too large you will get artifacts.
// A larger minimum increases the size of the AO 'blobs' around distant obstructions.
#define MIN_CONE_RADIUS 1
// See uniforms for the other two constants

// We threshold shadow values from cone tracing to eliminate 'almost obstructed' and 'almost unobstructed' artifacts
#define FULLY_SHADOWED_THRESHOLD 0.03
#define UNSHADOWED_THRESHOLD 0.97

// HACK: Adjusts the threshold for obstruction compensation so that the sample point must be
//  an additional distance beyond the edge of the obstruction to count
#define OBSTRUCTION_FUDGE 0.05

// Scale all [0-1] accumulators/values by this to avoid round-to-zero issues
#define DENORMAL_HACK 100


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

// See min growth rate constant above
uniform float DistanceFieldMaxConeRadius;

// The rate the cone is allowed to grow per-pixel
uniform float DistanceFieldConeGrowthRate;

// Occlusion values are mapped to opacity values via this exponent
uniform float  DistanceFieldOcclusionToOpacityPower;

// Traces always walk at least this many pixels per step
uniform float  DistanceFieldMinimumStepSize;


uniform float  DistanceFieldInvScaleFactor;
uniform float3 DistanceFieldTextureSliceCount;
uniform float2 DistanceFieldTextureSliceSize;
uniform float2 DistanceFieldTextureTexelSize;

Texture2D DistanceFieldTexture        : register(t4);
sampler   DistanceFieldTextureSampler : register(s4) {
    Texture = (DistanceFieldTexture);
    MipFilter = POINT;
    MinFilter = DISTANCE_FIELD_FILTER;
    MagFilter = DISTANCE_FIELD_FILTER;
};

float2 computeDistanceFieldSliceUv (
    float sliceIndex
) {
    sliceIndex = clamp(sliceIndex, 0, DistanceFieldTextureSliceCount.z - 1) / 2;
    float columnIndex = floor(fmod(sliceIndex, DistanceFieldTextureSliceCount.x));
    float rowIndex    = floor(sliceIndex / DistanceFieldTextureSliceCount.x);
    return float2(columnIndex * DistanceFieldTextureSliceSize.x, rowIndex * DistanceFieldTextureSliceSize.y);
}

float2 computeDistanceFieldSubsliceUv (
    float2 positionPx
) {
    return clamp(positionPx * DistanceFieldTextureTexelSize, float2(0, 0), DistanceFieldTextureSliceSize);
}

float sampleDistanceField (
    float3 position
) {
    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    float slicePosition = clamp(position.z / ZDistanceScale * DistanceFieldTextureSliceCount.z, 0, DistanceFieldTextureSliceCount.z);
    float sliceIndex1 = floor(slicePosition);
    float subslice = slicePosition - sliceIndex1;
    float sliceIndex2 = sliceIndex1 + 1;

    float2 uv = computeDistanceFieldSubsliceUv(position.xy);
    
    float2 sample1 = tex2Dgrad(
        DistanceFieldTextureSampler, uv + computeDistanceFieldSliceUv(sliceIndex1), 0, 0
    );
    float2 sample2 = tex2Dgrad(
        DistanceFieldTextureSampler, uv + computeDistanceFieldSliceUv(sliceIndex2), 0, 0
    );
    
    // FIXME: Somehow this r/g encoding introduces a consistent error along the z-axis compared to the old encoding?
    // It seems like floor instead of ceil fixes it but I have no idea why
    float evenSlice = floor(fmod(sliceIndex1, 2));
   
    return decodeDistance(lerp(
        lerp(sample1.r, sample1.g, evenSlice),
        lerp(sample2.g, sample2.r, evenSlice),
        subslice
    ));
}

float sampleAlongRay (
    float3 rayStart, float3 rayDirection, float distance
) {
    float3 samplePosition = rayStart + (rayDirection * distance);
    float sample = sampleDistanceField(samplePosition);
    return sample;
}

void coneTraceStep (
    in    float3 traceStart,
    in    float3 traceVector,
    in    float  traceLength,
    in    float  minStepSize,
    in    float3 coneRadiusRamp,
    inout float  previousOffset, 
    inout float  previousDistance, 
    inout float  previousDerivative,
    inout float  traceOffset,
    inout float  visibility
) {
    float distanceToObstacle = sampleAlongRay(traceStart, traceVector, traceOffset);

    float derivative = (distanceToObstacle - previousDistance) / (traceOffset - previousOffset);
    float secondOrderDerivative = (derivative - previousDerivative) / (traceOffset - previousOffset);

    previousDerivative = derivative;
    previousDistance = distanceToObstacle;
    previousOffset = traceOffset;

    float coneRadius = lerp(coneRadiusRamp.x, coneRadiusRamp.y, traceOffset / coneRadiusRamp.z);
    float localVisibility = clamp(distanceToObstacle, 0, coneRadius) * DENORMAL_HACK / coneRadius;
    visibility = min(visibility, localVisibility);

    traceOffset = traceOffset + max(abs(distanceToObstacle) * PARTIAL_STEP_SIZE, minStepSize);
}

float coneTrace (
    in float3 lightCenter,
    in float2 lightRamp,
    in float3 shadedPixelPosition,
    in bool   obstructionCompensation
) {
    // HACK: Compensate for Z scaling
    lightCenter.z *= ZDistanceScale;
    shadedPixelPosition.z *= ZDistanceScale;

    float minStepSize = max(1, DistanceFieldMinimumStepSize);
    float3 traceVector = (lightCenter - shadedPixelPosition);
    // HACK: We reduce the length of the trace so that we stop at the closest point on the surface of the light.
    float traceLength = max(length(traceVector) - lightRamp.x, 0);
    float traceOffset = 0;
    traceVector = normalize(traceVector);

    float3 coneRadiusRamp = float3(
        MIN_CONE_RADIUS,
        clamp(lightRamp.x, MIN_CONE_RADIUS, DistanceFieldMaxConeRadius),
        traceLength
    );

    // HACK
    coneRadiusRamp.x = coneRadiusRamp.y;

    float fst = FULLY_SHADOWED_THRESHOLD * DENORMAL_HACK;
    float visibility = 1.0 * DENORMAL_HACK;
    bool abort = false;

    // If the shaded point is completely within an obstacle we kill the trace early
    float distanceAtShadedPoint = sampleDistanceField(shadedPixelPosition);
    if (distanceAtShadedPoint < OBSTRUCTION_FUDGE)
        return 0;

    // Sample a half-step backward along the ray to compute a 'previous' derivative and prime the loop
    float previousOffset     = -distanceAtShadedPoint * 0.5;
    float previousDistance   = sampleAlongRay(shadedPixelPosition, traceVector, previousOffset);
    float previousDerivative = (distanceAtShadedPoint - previousDistance) / abs(previousOffset);

    // FIXME: Did I get this right? Should always do a step at the beginning and end of the ray
    [loop]
    while (!abort) {
        abort = (traceOffset >= traceLength) || 
            (visibility < fst);
        if (abort)
            traceOffset = traceLength;

        coneTraceStep(
            shadedPixelPosition, traceVector, traceLength, minStepSize, coneRadiusRamp,
            previousOffset, previousDistance, previousDerivative,
            traceOffset, visibility
        );
    }

    return pow(
        clamp(
            clamp((visibility - fst) / DENORMAL_HACK, 0, 1) / 
            (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD),
            0, 1
        ), 
        DistanceFieldOcclusionToOpacityPower
    );
}