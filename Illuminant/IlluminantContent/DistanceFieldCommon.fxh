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
// Not recommended, actually! But hey, maybe it'll help.
#define PARTIAL_STEP_SIZE 1

// The minimum and maximum approximate cone tracing radius
// The radius increases as the cone approaches the light source
// Raising the maximum produces soft shadows, but if it's too large you will get artifacts.
// A larger minimum increases the size of the AO 'blobs' around distant obstructions.
#define MIN_CONE_RADIUS 1
// See uniforms for the other two constants

// We threshold shadow values from cone tracing to eliminate 'almost obstructed' and 'almost unobstructed' artifacts
#define FULLY_SHADOWED_THRESHOLD 0.05
#define UNSHADOWED_THRESHOLD 0.95

// HACK: Adjusts the threshold for obstruction compensation so that the sample point must be
//  an additional distance beyond the edge of the obstruction to count
#define OBSTRUCTION_FUDGE 0.05

// Scale all [0-1] accumulators/values by this to avoid round-to-zero issues
#define DENORMAL_HACK 100

// HACK: Placeholder for uninitialized distance value
#define SHRUG -9999


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

float conePenumbra (
    float traceOffset,
    float traceLength,
    float distanceToObstacle
) {
    // FIXME: Instead of ramping the cone radius at start/end of trace, just adjust the distance value?

    // FIXME: This creates unoccluded voids when the light is at the center of a floating occluder
    /*
    float localRadius = max(
        min(
            min(
                DistanceFieldMaxConeRadius,
                // But we want to ramp the radius back down as we approach the end of the trace, otherwise
                //  objects *past* the end of the trace will count as occluders
                (traceLength - traceOffset) * DistanceFieldConeGrowthRate
            ),
            // And we want to ramp the radius down for points close to the beginning of the trace, as well
            traceOffset * DistanceFieldConeGrowthRate
        ),
        // And we can't go lower than the minimum cone size
        MIN_CONE_RADIUS
    );
    */

    float localRadius = DistanceFieldMaxConeRadius;

    distanceToObstacle *= DENORMAL_HACK;
    float result = clamp(distanceToObstacle / localRadius, 0, DENORMAL_HACK);

    return result;
}

void coneTraceStep (
    in    float3 traceStart,
    in    float3 traceVector,
    in    float  traceLength,
    in    float  minStepSize, 
    in    float  distanceAtShadedPoint,
    in    float  distanceAtLight,
    inout bool   obstructionCompensation,
    inout float  traceOffset,
    inout float  coneAttenuation
) {
    float distanceToObstacle = sampleAlongRay(traceStart, traceVector, traceOffset);

    if (obstructionCompensation) {
        // HACK: When shading a pixel that is known be in/on an obstruction, we ignore
        //  trace samples until we have successfully left the obstruction
        // Obstruction compensation is based on the expectation that the obstacle ends at
        //  -initialDistance along the ray, which is... usually true
        float expectedDistance = distanceAtShadedPoint + traceOffset - OBSTRUCTION_FUDGE;
        
        if (distanceToObstacle < expectedDistance)
            obstructionCompensation = false;
    } else {
        float penumbra = conePenumbra(traceOffset, traceLength, distanceToObstacle);
        coneAttenuation = min(coneAttenuation, penumbra);
    }

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
    float traceOffset = 0;
    float3 traceVector = (lightCenter - shadedPixelPosition);
    float traceLength = length(traceVector);
    traceVector = normalize(traceVector);

    float coneAttenuation = 1.0 * DENORMAL_HACK;
    bool abort = false;

    // If the shaded point is completely within an obstacle we kill the trace early
    float distanceAtShadedPoint = sampleDistanceField(shadedPixelPosition);
    if ((distanceAtShadedPoint < OBSTRUCTION_FUDGE) && !obstructionCompensation)
        return 0;

    float distanceAtLight       = sampleDistanceField(lightCenter);
    float fst = FULLY_SHADOWED_THRESHOLD * DENORMAL_HACK;

    // FIXME: Did I get this right? Should always do a step at the beginning and end of the ray
    [loop]
    while (!abort) {
        abort = (traceOffset >= traceLength) || (coneAttenuation <= fst);
        if (abort)
            traceOffset = traceLength;

        coneTraceStep(
            shadedPixelPosition, traceVector, traceLength, minStepSize,
            distanceAtShadedPoint, distanceAtLight,
            obstructionCompensation, traceOffset, coneAttenuation
        );
    }

    return pow(
        clamp(
            clamp((coneAttenuation - fst) / DENORMAL_HACK, 0, 1) / 
            (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD),
            0, 1
        ), 
        DistanceFieldOcclusionToOpacityPower
    );
}