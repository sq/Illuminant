// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

#define DISTANCE_POSITIVE_RANGE DISTANCE_ZERO
#define DISTANCE_NEGATIVE_RANGE (1 - DISTANCE_ZERO)

// Maximum positive (outside) distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_POSITIVE_MAX 128

// Maximum negative (inside) distance
#define DISTANCE_NEGATIVE_MAX 48

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
#define MAX_CONE_RADIUS 12

// We threshold shadow values from cone tracing to eliminate 'almost obstructed' and 'almost unobstructed' artifacts
#define FULLY_SHADOWED_THRESHOLD 0.015
#define UNSHADOWED_THRESHOLD 0.985

// HACK: Adjusts the threshold for obstruction compensation so that the sample point must be
//  an additional distance beyond the edge of the obstruction to count
#define OBSTRUCTION_FUDGE 0.1

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
    if (distance >= 0) {
        return DISTANCE_ZERO - ((distance / DISTANCE_POSITIVE_MAX) * DISTANCE_POSITIVE_RANGE);
    } else {
        return DISTANCE_ZERO + ((-distance / DISTANCE_NEGATIVE_MAX) * DISTANCE_NEGATIVE_RANGE);
    }
}

float decodeDistance (float encodedDistance) {
    if (encodedDistance <= DISTANCE_ZERO)
        return (DISTANCE_ZERO - encodedDistance) * (DISTANCE_POSITIVE_MAX / DISTANCE_POSITIVE_RANGE);
    else
        return (encodedDistance - DISTANCE_ZERO) * -(DISTANCE_NEGATIVE_MAX / DISTANCE_NEGATIVE_RANGE);
}

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

float2 computeDistanceFieldUv (
    float2 positionPx, float sliceIndex
) {
    sliceIndex = clamp(sliceIndex, 0, DistanceFieldTextureSliceCount.z - 1);
    float columnIndex = floor(sliceIndex % DistanceFieldTextureSliceCount.x);
    float rowIndex    = floor(sliceIndex / DistanceFieldTextureSliceCount.x);
    float2 uv = clamp(positionPx * DistanceFieldTextureTexelSize, float2(0, 0), DistanceFieldTextureSliceSize);
    return uv + float2(columnIndex * DistanceFieldTextureSliceSize.x, rowIndex * DistanceFieldTextureSliceSize.y);
}

float sampleDistanceField (
    float3 position
) {
    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    float slicePosition = clamp(position.z / ZDistanceScale * DistanceFieldTextureSliceCount.z, 0, DistanceFieldTextureSliceCount.z);
    float sliceIndex1 = floor(slicePosition);
    float subslice = slicePosition - sliceIndex1;
    float sliceIndex2 = sliceIndex1 + 1;
    
    // FIXME: Read appropriate channel here (.a for alpha8, .r for everything else)
    float distance1 = decodeDistance(tex2Dgrad(
        DistanceFieldTextureSampler, computeDistanceFieldUv(position.xy, sliceIndex1), 0, 0
    ).r);

    float distance2 = decodeDistance(tex2Dgrad(
        DistanceFieldTextureSampler, computeDistanceFieldUv(position.xy, sliceIndex2), 0, 0
    ).r);
    
    return lerp(distance1, distance2, subslice);
}

float sampleAlongRay (
    float3 rayStart, float3 rayDirection, float distance
) {
    float3 samplePosition = rayStart + (rayDirection * distance);
    float sample = sampleDistanceField(samplePosition);
    return sample;
}

float conePenumbra (
    float3 ramp,
    float  traceOffset,
    float  traceLength,
    float  distanceToObstacle
) {
    float localRadius = min(
        // Cone radius increases as we travel along the trace
        lerp(ramp.x, ramp.y, clamp(traceOffset * ramp.z, 0, 1)),
        // But we want to ramp the radius back down as we approach the end of the trace, otherwise
        //  objects *past* the end of the trace will count as occluders
        traceLength - traceOffset
    );
    float result = clamp(distanceToObstacle / localRadius, 0, 1);

    return result;
}

void coneTraceStep (
    in    float3 traceStart,
    in    float3 traceVector,
    in    float  traceLength,
    in    float  minStepSize, 
    in    float3 ramp,
    inout float  initialDistance,
    inout bool   obstructionCompensation,
    inout float  traceOffset,
    inout float  coneAttenuation
) {
    float distanceToObstacle = sampleAlongRay(traceStart, traceVector, traceOffset);

    // HACK: When shading a pixel that is known to begin in an obstruction, we ignore
    //  obstruction samples until we have successfully left the obstruction
    if (obstructionCompensation) {
        if (initialDistance == SHRUG)
            initialDistance = distanceToObstacle;

        float expectedDistance = initialDistance + traceOffset - OBSTRUCTION_FUDGE;
        
        if (distanceToObstacle < expectedDistance)
            obstructionCompensation = false;
    } else {
        float penumbra = conePenumbra(ramp, traceOffset, traceLength, distanceToObstacle);
        coneAttenuation = min(coneAttenuation, penumbra);
    }

    traceOffset += max(abs(distanceToObstacle) * PARTIAL_STEP_SIZE, minStepSize);
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
    float3 ramp = float3(MIN_CONE_RADIUS, min(lightRamp.x, MAX_CONE_RADIUS), rcp(max(lightRamp.y, 1)));
    float traceOffset = 0;
    float3 traceVector = (lightCenter - shadedPixelPosition);
    float traceLength = length(traceVector);
    traceVector = normalize(traceVector);

    float initialDistance = SHRUG;
    float coneAttenuation = 1.0;

    while (traceOffset < traceLength) {
        coneTraceStep(
            shadedPixelPosition, traceVector, traceLength, minStepSize, ramp, 
            initialDistance, obstructionCompensation, traceOffset, coneAttenuation
        );

        if (coneAttenuation <= FULLY_SHADOWED_THRESHOLD)
            break;
    }

    // HACK: Do an extra sample at the end directly in front of the light.
    // This eliminates banding artifacts when the light and the shaded pixel are both close to an obstruction.
    if (coneAttenuation > FULLY_SHADOWED_THRESHOLD) {
        traceOffset = traceLength;
        coneTraceStep(
            shadedPixelPosition, traceVector, traceLength, minStepSize, ramp, 
            initialDistance, obstructionCompensation, traceOffset, coneAttenuation
        );
    }

    return clamp((coneAttenuation - FULLY_SHADOWED_THRESHOLD) / (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD), 0, 1);
}