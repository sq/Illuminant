float evaluateNone (
    float3 worldPosition, float3 center, float3 size
) {
    return 0;
}

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
    float3 pl = position / size;
    float l = length(pl) - 1.0;
    float resultDistance =
        l * min(min(size.x, size.y), size.z);

    return resultDistance;
}

float evaluateCylinder (
    float3 worldPosition, float3 center, float3 size
) {
    const float bigValue = 65536.0;
    float bigEllipsoid = evaluateEllipsoid(worldPosition, center, float3(size.x, size.y, bigValue));
    float boxCap       = evaluateBox(worldPosition, center, float3(bigValue, bigValue, size.z));
    return max(bigEllipsoid, boxCap);
}

float evaluateByTypeId (
    int typeId, float3 worldPosition, float3 center, float3 size
) {
    // HACK: The compiler insists that typeId must be known-positive, so we force it
    switch (abs(typeId)) {
        case 1:
            return evaluateEllipsoid(worldPosition, center, size);
        case 2:
            return evaluateBox(worldPosition, center, size);
        case 3:
            return evaluateCylinder(worldPosition, center, size);
        default:
        case 0:
            return evaluateNone(worldPosition, center, size);
    }
}