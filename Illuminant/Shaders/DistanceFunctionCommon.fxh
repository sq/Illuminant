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

float evaluateSphere (
    float3 worldPosition, float3 center, float3 size, float rotation
) {
    float3 position = worldPosition - center;
    float radius = length(size);
    return length(position) - radius;
}

// generic ellipsoid - simple but bad approximated distance
float sdEllipsoid_nonlinear( in float3 p, in float3 r ) 
{
    return (length(p/r)-1.0)*min(min(r.x,r.y),r.z);
}

// generic ellipsoid - improved approximated distance
float sdEllipsoid_improvedV1( in float3 p, in float3 r ) 
{
    float k0 = length(p/r);
    float k1 = length(p/(r*r));
    return k0*(k0-1.0)/k1;
}

// IQ improved - from https://www.shadertoy.com/view/3s2fRd
float sdEllipsoid_improvedV2( in float3 p, in float3 r ) 
{
    float k0 = length(p / r);
    float k1 = length(p / (r * r));
    return (k0 < 1.0) 
        ? (k0 - 1.0) * min(min(r.x, r.y), r.z) 
        : k0 * (k0 - 1.0) / k1;
}

float evaluateEllipsoid (
    float3 worldPosition, float3 center, float3 size, float rotation
) {
    float3 position = worldPosition - center;
    position = rotateLocalPosition(position, rotation);

    return sdEllipsoid_improvedV2(position, size);
}

float sdCappedCylinder (float3 p, float h, float r) {
    float2 d = abs(float2(length(p.xy),p.z)) - float2(r,h);
    return min(max(d.x,d.y),0.0) + length(max(d,0.0));
}

float evaluateCylinder (
    float3 worldPosition, float3 center, float3 size, float rotation
) {
    float3 position = worldPosition - center;
    position = rotateLocalPosition(position, rotation);

    return sdCappedCylinder(position, size.z, length(size.xy));

    /* FIXME: This enables independent x/y size but has other garbage side effects
    const float bigValue = 65536.0;
    float bigEllipsoid = evaluateEllipsoid(worldPosition, center, float3(size.x, size.y, bigValue), rotation);
    float boxCap       = evaluateBox(worldPosition, center, float3(bigValue, bigValue, size.z), rotation);
    return max(bigEllipsoid, boxCap);
    */
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
        case 4:
            return evaluateSphere(worldPosition, center, size, rotation);
        default:
        case 0:
            return evaluateNone(worldPosition, center, size, rotation);
    }
}

#endif