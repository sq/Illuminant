#define PI 3.14159265359

// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

// Maximum distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_MAX 256

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR

// The minimum and maximum approximate cone tracing radius
// The radius increases as the cone approaches the light source
// Raising the maximum produces soft shadows, but if it's too large you will get artifacts.
// A larger minimum increases the size of the AO 'blobs' around distant obstructions.
#define MIN_CONE_RADIUS 0.5
#define MAX_ANGLE_DEGREES 10
// See uniforms for the other two constants

// As we approach the maximum number of steps we ramp visibility down to 0.
// Otherwise, we get gross 'false visibility' artifacts inside early-terminated traces
//  (most, if not all, early-terminated traces are occluded in practice)
#define MAX_STEP_RAMP_WINDOW 2

// HACK: Start the trace a certain number of pixels (along the trace) away from the shaded point.
// This mitigates erroneous self-occlusion
// This works better if you offset the shaded point forwards along the surface normal.
#define TRACE_INITIAL_OFFSET_PX 1

// We threshold shadow values from cone tracing to eliminate 'almost obstructed' and 'almost unobstructed' artifacts
#define FULLY_SHADOWED_THRESHOLD 0.03
#define UNSHADOWED_THRESHOLD 0.97


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

// The maximum radius of the cone
uniform float  DistanceFieldMaxConeRadius;

// The maximum number of steps to take when cone tracing
uniform float  DistanceFieldMaxStepCount;

// Occlusion values are mapped to opacity values via this exponent
uniform float  DistanceFieldOcclusionToOpacityPower;

// Traces always walk at least this many pixels per step
uniform float  DistanceFieldMinimumStepSize;

// Scales the length of long steps taken outside objects
uniform float  DistanceFieldLongStepFactor;

// The world position that corresponds to a distance field texture coordinate of [1,1,1]
uniform float3 DistanceFieldExtent;

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
    float scaledPositionZ = position.z / DistanceFieldExtent.z;
    float slicePosition = clamp(scaledPositionZ * DistanceFieldTextureSliceCount.z, 0, DistanceFieldTextureSliceCount.z - 1);
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
   
    float decodedDistance = decodeDistance(lerp(
        lerp(sample1.r, sample1.g, evenSlice),
        lerp(sample2.g, sample2.r, evenSlice),
        subslice
    ));

    // HACK: Samples outside the distance field will be wrong if they just
    //  read the closest distance in the field.
    float3 minVolumeExtent = float3(0, 0, 0);
    float3 maxVolumeExtent = DistanceFieldExtent;
    float clampedPosition = clamp(position, minVolumeExtent, maxVolumeExtent);
    float distanceToVolume = length(clampedPosition - position);

    return decodedDistance; // + distanceToVolume;
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
    in    float3 coneRadiusSettings,
    inout float  traceOffset,
    inout float  visibility
) {
    // We approximate the cone with a series of spheres
    float localSphereRadius = clamp(traceOffset * coneRadiusSettings.z, coneRadiusSettings.x, coneRadiusSettings.y);

    float distanceToObstacle = sampleAlongRay(traceStart, traceVector, traceOffset);

    float localVisibility = clamp(distanceToObstacle, 0, localSphereRadius) / localSphereRadius;
    visibility = min(visibility, localVisibility);

    float stepSize = max(
        abs(distanceToObstacle) * (
            // Steps outside of objects can be scaled to be longer/shorter to adjust the quality
            //  of partial visibility areas
            (distanceToObstacle < 0)
                ? 1
                : DistanceFieldLongStepFactor
        ), minStepSize
    );

    traceOffset = traceOffset + stepSize;
}

float coneTrace (
    in float3 lightCenter,
    in float2 lightRamp,
    in float3 shadedPixelPosition
) {
    float minStepSize = max(1, DistanceFieldMinimumStepSize);

    float traceOffset = TRACE_INITIAL_OFFSET_PX;

    float3 traceVector = (lightCenter - shadedPixelPosition);
    float  traceLength = length(traceVector);
    traceVector = normalize(traceVector);

    float maxTangentAngle = tan(MAX_ANGLE_DEGREES * PI / 180.0f);
    float lightTangentAngle = min(lightRamp.x / traceLength, maxTangentAngle);

    float3 coneRadiusSettings = float3(
        MIN_CONE_RADIUS,
        clamp(lightRamp.x, MIN_CONE_RADIUS, DistanceFieldMaxConeRadius),
        lightTangentAngle
    );

    float visibility = 1.0;
    bool abort = false;

    float stepCount = 0;

    [loop]
    while (!abort) {
        abort = 
            (stepCount >= DistanceFieldMaxStepCount) ||
            (traceOffset >= traceLength) || 
            (visibility < FULLY_SHADOWED_THRESHOLD);
        if (abort)
            traceOffset = traceLength;

        coneTraceStep(
            shadedPixelPosition, traceVector, traceLength, 
            minStepSize, coneRadiusSettings, 
            traceOffset, visibility
        );
        stepCount += 1;
    }

    // HACK: Force visibility down to 0 if we are going to terminate the trace because we took too many steps.
    float windowStart = max(DistanceFieldMaxStepCount - MAX_STEP_RAMP_WINDOW, 0);
    float stepWindowVisibility = (1.0 - (stepCount - windowStart) / MAX_STEP_RAMP_WINDOW);
    visibility = min(visibility, stepWindowVisibility);

    return pow(
        clamp(
            clamp((visibility - FULLY_SHADOWED_THRESHOLD), 0, 1) / 
            (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD),
            0, 1
        ), 
        DistanceFieldOcclusionToOpacityPower
    );
}