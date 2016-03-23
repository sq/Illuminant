// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

#define DISTANCE_POSITIVE_RANGE DISTANCE_ZERO
#define DISTANCE_NEGATIVE_RANGE (1 - DISTANCE_ZERO)

// Maximum positive (outside) distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_POSITIVE_MAX 96

// Maximum negative (inside) distance
#define DISTANCE_NEGATIVE_MAX 32

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR

// HACK: Step by a fraction of the distance to the next object for better accuracy
#define PARTIAL_STEP_SIZE 0.9

// The minimum and maximum approximate cone tracing radius
// The cone grows larger as light travels from the source, up to the maximum
// Raising the maximum produces soft shadows, but if it's too large you will get artifacts.
// A larger maximum also increases the size of the AO 'blobs' around distant obstructions.
#define MIN_CONE_RADIUS 2
#define MAX_CONE_RADIUS 24

// We threshold shadow values from cone tracing to eliminate 'almost obstructed' and 'almost unobstructed' artifacts
#define FULLY_SHADOWED_THRESHOLD 0.05
#define UNSHADOWED_THRESHOLD 0.95

// We scale distance values into [0, 1] so we can use the depth buffer to do a cheap min()
#define DISTANCE_DEPTH_MAX 1024.0
#define DISTANCE_DEPTH_OFFSET 0.25

// Distances <= this are considered 'inside'
#define INTERIOR_THRESHOLD 0.25


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

float distanceToDepth (float distance) {
    if (distance < 0) { 
        return clamp(DISTANCE_DEPTH_OFFSET + (distance / 256), 0, DISTANCE_DEPTH_OFFSET);
    } else {
        return clamp(DISTANCE_DEPTH_OFFSET + (distance / DISTANCE_DEPTH_MAX), DISTANCE_DEPTH_OFFSET, 1); 
    }
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
    float slicePosition = clamp(position.z / ZDistanceScale * DistanceFieldTextureSliceCount.z, 0, DistanceFieldTextureSliceCount.z - 1);
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
    float  distanceFromLight,
    float  distanceToObstacle
) {
    // FIXME: Cancel out shadowing as we approach the target point somehow?
    float localRadius = lerp(ramp.x, ramp.y, clamp(distanceFromLight * ramp.z, 0, 1));
    float result = clamp(distanceToObstacle / localRadius, 0, 1);

    return result;
}

float coneTraceStep (
    in float3 traceStart,
    in float3 traceVector,
    in float  traceOffset,
    in float3 ramp,
    inout float coneAttenuation
) {
    float distanceToObstacle = sampleAlongRay(traceStart, traceVector, traceOffset);

    float penumbra = conePenumbra(ramp, traceOffset, distanceToObstacle);
    coneAttenuation = min(coneAttenuation, penumbra);

    return distanceToObstacle;
}

float coneTrace (
    in float3 lightCenter,
    in float2 lightRamp,
    in float3 shadedPixelPosition,
    in float  interiorBrightness
) {
    // HACK: Compensate for Z scaling
    lightCenter.z *= ZDistanceScale;
    shadedPixelPosition.z *= ZDistanceScale;

    float minStepSize = max(1, DistanceFieldMinimumStepSize);
    float3 ramp = float3(MIN_CONE_RADIUS, min(lightRamp.x, MAX_CONE_RADIUS), rcp(max(lightRamp.y, 1)));
    float traceOffset = 0;
    float3 traceVector = (shadedPixelPosition - lightCenter);
    float traceLength = length(traceVector);
    traceVector = normalize(traceVector);

    float coneAttenuation = 1.0;

    while (traceOffset < traceLength) {
        float distanceToObstacle = coneTraceStep(
            lightCenter, traceVector, traceOffset,
            ramp, coneAttenuation
        );

        if (coneAttenuation <= FULLY_SHADOWED_THRESHOLD)
            break;

        traceOffset += max(abs(distanceToObstacle) * PARTIAL_STEP_SIZE, minStepSize);
    }

    // HACK: Do an extra sample at the end directly at the shaded pixel.
    // This eliminates weird curved banding artifacts close to obstructions.
    if (coneAttenuation > FULLY_SHADOWED_THRESHOLD) {
        coneTraceStep(
            lightCenter, traceVector, traceLength,
            ramp, coneAttenuation
        );
    }

    return clamp((coneAttenuation - FULLY_SHADOWED_THRESHOLD) / (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD), 0, 1);
}