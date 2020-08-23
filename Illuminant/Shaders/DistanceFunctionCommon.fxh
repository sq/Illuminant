// Most of these formulas are derived from inigo quilez's work
// http://iquilezles.org/www/articles/distfunctions/distfunctions.htm

#ifndef DISTANCE_FUNCTION_DEFINED
#define DISTANCE_FUNCTION_DEFINED

float evaluateNone (
    float3 worldPosition, float3 center, float3 size, float rotation
) {
    return 0;
}

float3 rotateLocalPosition (float3 localPosition, float rotation) {
    float _sin, _cos;
    sincos(-rotation, _sin, _cos);
    return float3(
        (_cos * localPosition.x) - (_sin * localPosition.y),
        (_sin * localPosition.x) + (_cos * localPosition.y),
        localPosition.z
    );
}

float evaluateBox (
    float3 worldPosition, float3 center, float3 size, float rotation
) {
    float3 position = worldPosition - center;
    position = rotateLocalPosition(position, rotation);

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
    float3 worldPosition, float3 center, float3 size, float rotation
) {
    float3 position = worldPosition - center;

    // FIXME: This seems spherical?
    float3 pl = position / size;
    float l = length(pl) - 1.0;
    return l * min(min(size.x, size.y), size.z);
}

float evaluateCylinder (
    float3 worldPosition, float3 center, float3 size, float rotation
) {
    const float bigValue = 65536.0;
    float bigEllipsoid = evaluateEllipsoid(worldPosition, center, float3(size.x, size.y, bigValue), rotation);
    float boxCap       = evaluateBox(worldPosition, center, float3(bigValue, bigValue, size.z), rotation);
    return max(bigEllipsoid, boxCap);
}

float evaluateByTypeId (
    int typeId, float3 worldPosition, float3 center, float3 size, float rotation
) {
    // HACK: The compiler insists that typeId must be known-positive, so we force it
    switch (abs(typeId)) {
        case 1:
            return evaluateEllipsoid(worldPosition, center, size, rotation);
        case 2:
            return evaluateBox(worldPosition, center, size, rotation);
        case 3:
            return evaluateCylinder(worldPosition, center, size, rotation);
        default:
        case 0:
            return evaluateNone(worldPosition, center, size, rotation);
    }
}

#endif