// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

#define DISTANCE_POSITIVE_RANGE DISTANCE_ZERO
#define DISTANCE_NEGATIVE_RANGE (1 - DISTANCE_ZERO)

// Maximum positive (outside) distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_POSITIVE_MAX 64

// Maximum negative (inside) distance
#define DISTANCE_NEGATIVE_MAX 12

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR

// The minimum trace step size (in pixels)
// Higher values improve the worst-case performance of the trace, but introduce artifacts
#define MIN_STEP_SIZE 3

// The minimum and maximum approximate cone tracing radius
// The cone grows larger as light travels from the source, up to the maximum
// Raising the maximum produces soft shadows, but if it's too large you will get artifacts.
// A larger maximum also increases the size of the AO 'blobs' around distant obstructions.
#define MIN_CONE_RADIUS 1.5
#define MAX_CONE_RADIUS 16

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

uniform float2 DistanceFieldTextureTexelSize;

Texture2D DistanceFieldTexture        : register(t4);
sampler   DistanceFieldTextureSampler : register(s4) {
    Texture = (DistanceFieldTexture);
    MipFilter = POINT;
    MinFilter = DISTANCE_FIELD_FILTER;
    MagFilter = DISTANCE_FIELD_FILTER;
};

float sampleDistanceField (
    float2 positionPx
) {
    float2 uv = positionPx * DistanceFieldTextureTexelSize;
    // FIXME: Read appropriate channel here (.a for alpha8, .r for everything else)
    float raw = tex2Dgrad(DistanceFieldTextureSampler, uv, 0, 0).a;
    return decodeDistance(raw);
}

float sampleAlongRay (
    float3 rayStart, float3 rayDirection, float distance
) {
    float3 samplePosition = rayStart + (rayDirection * distance);
    float sample = sampleDistanceField(samplePosition.xy);
    return sample;
}

float conePenumbra (
    float3 ramp,
    float  distanceFromLight,
    float  traceLength,
    float  distanceToObstacle
) {
    // FIXME: Cancel out shadowing as we approach the target point somehow?
    float localRadius = lerp(ramp.x, ramp.y, clamp(distanceFromLight * ramp.z, 0, 1));
    float result = clamp(distanceToObstacle / localRadius, 0, 1);

    return result;
}

float coneTrace (
    in float3 lightCenter,
    in float2 lightRamp,
    in float3 shadedPixelPosition,
    in float  interiorBrightness
) {
    float3 ramp = float3(MIN_CONE_RADIUS, min(lightRamp.x, MAX_CONE_RADIUS), rcp(max(lightRamp.y, 1)));
    float traceOffset = 0;
    float3 traceVector = (shadedPixelPosition - lightCenter);
    float traceLength = length(traceVector);
    traceVector = normalize(traceVector);

    float coneAttenuation = 1.0;

    while (traceOffset < traceLength) {
        float distanceToObstacle = sampleAlongRay(lightCenter, traceVector, traceOffset);

        float penumbra = conePenumbra(ramp, traceOffset, traceLength, distanceToObstacle);
        coneAttenuation = min(coneAttenuation, penumbra);

        if (coneAttenuation <= FULLY_SHADOWED_THRESHOLD)
            break;

        traceOffset += max(abs(distanceToObstacle), MIN_STEP_SIZE);
    }

    // HACK: Do an extra sample at the end directly at the shaded pixel.
    // This eliminates weird curved banding artifacts close to obstructions.
    if (coneAttenuation > FULLY_SHADOWED_THRESHOLD)
    {
        float distanceToObstacle = sampleAlongRay(shadedPixelPosition, traceVector, 0);

        float penumbra = conePenumbra(ramp, traceOffset, traceLength, distanceToObstacle);
        coneAttenuation = min(coneAttenuation, penumbra);
    }

    return clamp((coneAttenuation - FULLY_SHADOWED_THRESHOLD) / (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD), 0, 1);
}