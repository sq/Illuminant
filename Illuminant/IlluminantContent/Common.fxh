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

// HACK: We offset all values because we can only clear the render target to 0.0 or 1.0 :(
//  This makes the pixels cleared to 0 by the gpu count as extremely distant
#define DISTANCE_OFFSET 1024.0

// HACK: Scale distance values into [0, 1] so we can use the depth buffer to do a cheap min()
#define DISTANCE_DEPTH_MAX 2048.0

float distanceToDepth (float distance) {
    // FIXME: The abs() here is designed to pick the 'closest' point, whether it's interior or exterior
    // We do interior/exterior in separate passes so that an exterior point never overrides an interior
    //  point, but we want to ensure that in the case of two intersecting volumes we actually produce
    //  the distance to the closest volume edge.
    return clamp(abs(distance / DISTANCE_DEPTH_MAX), 0, 1);
}

float4 encodeDistance (float distance) {
    float d = distance;
    return float4(d - DISTANCE_OFFSET, 1, 1, 1);
}

float decodeDistance (float4 encodedDistance) {
    return encodedDistance.r + DISTANCE_OFFSET;
}