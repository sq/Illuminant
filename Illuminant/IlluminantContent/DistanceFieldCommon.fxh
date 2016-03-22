//
// For integral distance field encoding

// This is a distance of 0
#define DISTANCE_ZERO (192.0 / 255.0)

//
// For floating-point distance field encoding
// #define FP_DISTANCE

// HACK: We offset all values because we can only clear the render target to 0.0 or 1.0 :(
//  This makes the pixels cleared to 0 by the gpu count as extremely distant
#define DISTANCE_OFFSET 768.0


//
// Cone tracing


#define MIN_CONE_RADIUS 1
#define MAX_CONE_RADIUS 18


//
// General

// HACK: Scale distance values into [0, 1] so we can use the depth buffer to do a cheap min()
#define DISTANCE_DEPTH_MAX 1024.0

// FIXME: DX9 can't filter half-float surfaces
#ifdef FP_DISTANCE
    #define DISTANCE_FIELD_FILTER POINT
#else
    #define DISTANCE_FIELD_FILTER LINEAR
#endif


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
        return clamp(0.25 + (distance / 256), 0, 0.25);
    } else {
        return clamp(0.25 + (distance / DISTANCE_DEPTH_MAX), 0.25, 1); 
    }
}

float4 encodeDistance (float distance) {
#ifdef FP_DISTANCE
    float d = distance;
    return d - DISTANCE_OFFSET;
#else
    if (distance >= 0) {
        return DISTANCE_ZERO - (distance / 70);
    } else {
        return DISTANCE_ZERO + (-distance / 12);
    }
#endif
}

float decodeDistance (float encodedDistance) {
#ifdef FP_DISTANCE
    return encodedDistance + DISTANCE_OFFSET;
#else
    if (encodedDistance <= DISTANCE_ZERO)
        return (DISTANCE_ZERO - encodedDistance) * 70;
    else
        return (encodedDistance - DISTANCE_ZERO) * -12;
#endif
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

float conePenumbra (
    float3 ramp,
    float  distanceFromLight,
    float  traceLength,
    float  distanceToObstacle
) {
    // FIXME: Cancel out shadowing as we approach the target point somehow?
    float localRadius = lerp(ramp.x, ramp.y, clamp(distanceFromLight * ramp.z, 0, 1));
    return clamp(distanceToObstacle / localRadius, 0, 1);
}

float isInside (float distance) {
    return distance <= 0.1;
}

float didGradientChange (
    inout float previousDistance,
    inout float previousGradient,
    in    float distanceToObstacle,
    out   float gradient
) {
    float distanceDelta = previousDistance - distanceToObstacle;

    if ((previousDistance <= 0) && (distanceToObstacle <= 0)) {
        // HACK: Interior is always forced to a specific gradient
        gradient = 0.0;
    } else if (abs(distanceDelta) >= 0.25) {
        gradient = sign(distanceDelta);
    } else {
        gradient = 0.0;
    }

    previousDistance = distanceToObstacle;

    if (gradient != previousGradient) {
        previousGradient = gradient;
        return 1.0;
    }

    return 0;
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
    float incompleteConeAttenuation = 1.0;
    float previousDistance = 9999;
    float previousGradient = 0.0, gradient;

    while (traceOffset < traceLength) {
        float3 tracePosition = lightCenter + (traceVector * traceOffset);
        float distanceToObstacle = sampleDistanceField(tracePosition.xy);

        if (
            didGradientChange(previousDistance, previousGradient, distanceToObstacle, gradient) > 0
        ) {
            coneAttenuation = min(coneAttenuation, incompleteConeAttenuation);
            incompleteConeAttenuation = 1.0;
        }

        float penumbra = conePenumbra(ramp, traceOffset, traceLength, distanceToObstacle);
        incompleteConeAttenuation = min(incompleteConeAttenuation, penumbra);

        traceOffset += max(abs(distanceToObstacle), 1);
    }

    {
        float distanceToObstacle = sampleDistanceField(shadedPixelPosition.xy);

        return coneAttenuation * lerp(1, interiorBrightness, 1.0 - clamp(distanceToObstacle - 0.85, 0, 1));
    }

    return coneAttenuation;
}