float evaluateBox (
    float3 worldPosition, float3 center, float3 size
) {
    float3 position = worldPosition - center;

    float3 d = abs(position) - size;
    float resultDistance =
        min(
            max(d.x, max(d.y, d.z)),
            0.0
            ) + length(max(d, 0.0)
                );

    return resultDistance;
}

float evaluateEllipsoid (
    float3 worldPosition, float3 center, float3 size
) {
    float3 position = worldPosition - center;

    // FIXME: Why is this a sphere???????????
    float l = length(position / size) - 1.0;
    float resultDistance =
        l * min(min(size.x, size.y), size.z);

    return resultDistance;
}

float evaluateCylinder (
    float3 worldPosition, float3 center, float3 size
) {
    float3 position = worldPosition - center;

    float3 p = position.xzy;
    float2 h = size.xz;

    float2 d = abs(float2(length(p.xz), p.y)) - h;
    float resultDistance = min(max(d.x, d.y), 0.0) + length(max(d, 0.0));

    return resultDistance;
}