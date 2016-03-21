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
// General

// HACK: Scale distance values into [0, 1] so we can use the depth buffer to do a cheap min()
#define DISTANCE_DEPTH_MAX 1024.0


float distanceToDepth (float distance) {
    // FIXME: The abs() here is designed to pick the 'closest' point, whether it's interior or exterior
    // We do interior/exterior in separate passes so that an exterior point never overrides an interior
    //  point, but we want to ensure that in the case of two intersecting volumes we actually produce
    //  the distance to the closest volume edge.
    return clamp(abs(distance / DISTANCE_DEPTH_MAX), 0, 1);
}

float4 encodeDistance (float distance) {
#ifdef FP_DISTANCE
    float d = distance;
    return d - DISTANCE_OFFSET;
#else
    float scaled = distance / 255.0;
    return clamp(DISTANCE_ZERO - scaled, 0, 1);
#endif
}

float decodeDistance (float4 encodedDistance) {
#ifdef FP_DISTANCE
    return encodedDistance.r + DISTANCE_OFFSET;
#else
    if (encodedDistance.a <= DISTANCE_ZERO)
        return (DISTANCE_ZERO - encodedDistance.a) * 255.0;
    else
        return (encodedDistance.a - DISTANCE_ZERO) * -255.0;
#endif
}

uniform float2 DistanceFieldTextureTexelSize;

// FIXME: DX9 can't filter half-float surfaces
#ifdef FP_DISTANCE
    #define DISTANCE_FIELD_FILTER POINT
#else
    #define DISTANCE_FIELD_FILTER LINEAR
#endif

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
    float raw = tex2Dgrad(DistanceFieldTextureSampler, uv, 0, 0).r;
    return decodeDistance(raw);
}