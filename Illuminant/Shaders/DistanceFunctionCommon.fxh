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

// To use these you'll want the 2D distance field functions from SDF2D.fxh in Squared.Render

float _extrude2D (float3 worldPosition, float d, float h) {
    float2 w = float2(d, abs(worldPosition.z) - h);
    return min(max(w.x, w.y), 0.0) + length(max(w, 0.0));
}

float2 _revolve2D (float3 worldPosition, float o) {
    return float2(length(worldPosition.xz) - o, worldPosition.y);
}

#define extrudeSDF2D(func, worldPosition, center, size, rotation) _extrude2D(worldPosition - center, func(worldPosition.xy, center.xy, size.xy, rotation), size.z)
// FIXME: Probably incorrect for rotation != 0
#define revolveSDF2D(func, worldPosition, center, size, rotation) func(_revolve2D(worldPosition - center, size.z), 0, size.xy, rotation)

#endif