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

#define DISTANCE_MAX 127.0

float distanceToDepth (float distance) {
    // FIXME: The abs() here is designed to pick the 'closest' point, whether it's interior or exterior
    // We do interior/exterior in separate passes so that an exterior point never overrides an interior
    //  point, but we want to ensure that in the case of two intersecting volumes we actually produce
    //  the distance to the closest volume edge.
    return clamp(abs(distance / DISTANCE_MAX), 0, 1);
}

float4 encodeDistance (float distance) {
    float d = (distance / DISTANCE_MAX) + 0.5;
    return float4(d, d, d, 1);
}

float decodeDistance (float encodedDistance) {
    return (encodedDistance - 0.5) * DISTANCE_MAX;
}