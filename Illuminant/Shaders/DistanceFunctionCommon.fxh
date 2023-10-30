// Most of these formulas are derived from inigo quilez's work
// http://iquilezles.org/www/articles/distfunctions/distfunctions.htm

#ifndef DISTANCE_FUNCTION_DEFINED
#define DISTANCE_FUNCTION_DEFINED

float evaluateNone (
    float3 worldPosition, float3 center, float3 size, float4 rotation
) {
    return 0;
}

// Quaternion multiplication
// http://mathworld.wolfram.com/Quaternion.html
float4 qmul(float4 q1, float4 q2) {
    return float4(
        q2.xyz * q1.w + q1.xyz * q2.w + cross(q1.xyz, q2.xyz),
        q1.w * q2.w - dot(q1.xyz, q2.xyz)
    );
}

// http://mathworld.wolfram.com/Quaternion.html
float3 rotateLocalPosition (float3 localPosition, float4 rotation) {
    float4 r_c = rotation * float4(-1, -1, -1, 1);
    return qmul(rotation, qmul(float4(localPosition, 0), r_c)).xyz;
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

float4 opElongate (float3 p, float3 h) {
    float3 q = abs(p) - h;
    return float4(sign(p) * max(q, 0.0), min(max(q.x, max(q.y, q.z)), 0.0));
}

float evaluateBox (
    float3 worldPosition, float3 center, float3 size, float4 rotation
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

float evaluateSpheroid (
    float3 worldPosition, float3 center, float3 size, float4 rotation
) {
    float3 position = worldPosition - center;
    position = rotateLocalPosition(position, rotation);

    float minSize = min(size.x, min(size.y, size.z));
    float3 elongation = size - minSize;
    float4 w = opElongate(position, elongation);
    return w.w + (length(w.xyz) - minSize);
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
    float3 worldPosition, float3 center, float3 size, float4 rotation
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
    float3 worldPosition, float3 center, float3 size, float4 rotation
) {
    float3 position = worldPosition - center;
    position = rotateLocalPosition(position, rotation);

    return sdCappedCylinder(position, size.z, length(size.xy));

    /* FIXME: This doesn't work, but should
    float minSize = min(size.x, size.y);
    float3 elongation = float3(size.xy - minSize, 0);
    float4 w = opElongate(position, elongation);

    return w.w + sdCappedCylinder(w.xyz, size.z, minSize);
    */

    /* FIXME: This enables independent x/y size but has other garbage side effects
    const float bigValue = 65536.0;
    float bigEllipsoid = evaluateEllipsoid(worldPosition, center, float3(size.x, size.y, bigValue), rotation);
    float boxCap       = evaluateBox(worldPosition, center, float3(bigValue, bigValue, size.z), rotation);
    return max(bigEllipsoid, boxCap);
    */
}

float sdOctogonPrism( float3 p, float r, float h )
{
  const float3 k = float3(-0.9238795325,   // sqrt(2+sqrt(2))/2 
                       0.3826834323,   // sqrt(2-sqrt(2))/2
                       0.4142135623 ); // sqrt(2)-1 
  // reflections
  p = abs(p);
  p.xy -= 2.0*min(dot(float2( k.x,k.y),p.xy),0.0)*float2( k.x,k.y);
  p.xy -= 2.0*min(dot(float2(-k.x,k.y),p.xy),0.0)*float2(-k.x,k.y);
  // polygon side
  p.xy -= float2(clamp(p.x, -k.z*r, k.z*r), r);
  float2 d = float2( length(p.xy)*sign(p.y), p.z-h );
  return min(max(d.x,d.y),0.0) + length(max(d,0.0));
}

float evaluateOctagon (
    float3 worldPosition, float3 center, float3 size, float4 rotation
) {
    float3 position = worldPosition - center;
    position = rotateLocalPosition(position, rotation);

    float minSize = min(size.x, size.y);
    float3 elongation = float3(size.xy - minSize, 0);
    float4 w = opElongate(position, elongation);

    return w.w + sdOctogonPrism(w.xyz, minSize, size.z);
}

float evaluateByTypeId (
    int typeId, float3 worldPosition, float3 center, float3 size, float4 rotation
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
            return evaluateSpheroid(worldPosition, center, size, rotation);
        case 5:
            return evaluateOctagon(worldPosition, center, size, rotation);
        default:
        case 0:
            return evaluateNone(worldPosition, center, size, rotation);
    }
}

#endif