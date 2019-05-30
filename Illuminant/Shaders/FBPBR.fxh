float3  rayPlaneIntersect(
    in  float3  rayOrigin , in  float3  rayDirection ,
    in  float3  planeOrigin , in  float3  planeNormal
) {
    float  distance = dot(planeNormal , planeOrigin  - rayOrigin) / dot(planeNormal , rayDirection);
    return  rayOrigin + rayDirection * distance;
}

//  Return  the  closest  point to a rectangular  shape  defined  by two  vectors
// left  and up
float3  closestPointRect(in  float3 pos , in  float3  planeOrigin , in  float3  left , in  float3 up, in
    float  halfWidth , in  float  halfHeight)
{
    float3  dir = pos - planeOrigin;
    // - Project  in 2D plane (forward  is the  light  direction  away  from
    //    the  plane)
    // - Clamp  inside  the  rectangle
    // - Calculate  new  world  position
    float2  dist2D = float2(dot(dir , left), dot(dir , up));
    float2  rectHalfSize = float2(halfWidth , halfHeight);
    dist2D = clamp2(dist2D , -rectHalfSize , rectHalfSize);
    return  planeOrigin + dist2D.x * left + dist2D.y * up;
}

float  rightPyramidSolidAngle(float dist , float  halfWidth , float  halfHeight)
{
    float a = halfWidth;
    float b = halfHeight;
    float h = dist;
    return 4 * asin(a * b / sqrt((a * a + h * h) * (b * b + h * h)));
}

float  rectangleSolidAngle(
    float3  worldPos ,
    float3 p0, float3 p1,
    float3 p2, float3  p3
) {
    float3  v0 = p0 - worldPos;
    float3  v1 = p1 - worldPos;
    float3  v2 = p2 - worldPos;
    float3  v3 = p3 - worldPos;
    float3  n0 = normalize(cross(v0, v1));
    float3  n1 = normalize(cross(v1, v2));
    float3  n2 = normalize(cross(v2, v3));
    float3  n3 = normalize(cross(v3, v0));
    float  g0 = acos(dot(-n0, n1));
    float  g1 = acos(dot(-n1, n2));
    float  g2 = acos(dot(-n2, n3));
    float  g3 = acos(dot(-n3, n0));
    return  g0 + g1 + g2 + g3 - 2 * PI;
}

float computeLineLightOpacity (
    float3 worldPos, float3 worldNormal,
    float3 P0, float3 P1,
    float4 lightProperties, 
    out float3 spherePosition, out float u
) {
    // FIXME: ????
    float3 lightLeft = normalize(P1 - P0);
    float3 lightCenter = lerp(P0, P1, 0.5);
    float lightWidth = length(P1 - P0);
    float lightRadius = lightProperties.x;

    // The  sphere  is  placed  at the  nearest  point  on the  segment.
    // The  rectangular  plane  is  define  by the  following  orthonormal  frame:
    spherePosition    = closestPointOnLineSegment3(P0, P1, worldPos, u);
    float3  forward   = normalize(spherePosition - worldPos);
    float3  left      = lightLeft;
    float3  up        = cross(lightLeft , forward);
    float3  p0 = P0 + lightRadius * up;
    float3  p1 = P0 - lightRadius * up;
    float3  p2 = P1 - lightRadius * up;
    float3  p3 = P1 + lightRadius * up;
    float  solidAngle = rectangleSolidAngle(worldPos , p0 , p1, p2, p3);
    float  illuminance = solidAngle * 0.2 * (
    saturate(dot(normalize(p0 - worldPos), worldNormal)) +
    saturate(dot(normalize(p1 - worldPos), worldNormal)) +
    saturate(dot(normalize(p2 - worldPos), worldNormal)) +
    saturate(dot(normalize(p3 - worldPos), worldNormal)) +
    saturate(dot(normalize(lightCenter  - worldPos), worldNormal)));
    // We then  add  the  contribution  of the  sphere
    float3  sphereUnormL      = spherePosition  - worldPos;
    float3  sphereL            = normalize(sphereUnormL);
    float  sqrSphereDistance = dot(sphereUnormL , sphereUnormL);
    float  illuminanceSphere = PI * saturate(dot(sphereL , worldNormal)) *
    ((lightRadius * lightRadius) / sqrSphereDistance);

    // HACK: The paper did a + here instead but that produces oddly shaped lobes at the ends and the center
    // Using max produces weird artifacts at long distances, unfortunately
    if (0)
        illuminance = max(illuminance, illuminanceSphere);
    else
        illuminance = illuminance + illuminanceSphere;

    // If you remove the saturate this overbrightens in a really sick way
    if (1)
        return saturate(illuminance);
    else
        return illuminance;
}

// A right  disk is a disk  oriented  to  always  face  the  lit  surface.
//  Solid  angle  of a sphere  or a right  disk is 2 PI (1 - cos(subtended  angle)).
//  Subtended  angle  sigma = arcsin(r / d) for a sphere
// and  sigma = atan(r / d) for a right  disk
//  sinSigmaSqr = sin(subtended  angle)^2, it is (r^2 / d^2) for a sphere
// and (r^2 / ( r^2 + d^2)) for a disk
//  cosTheta  is not  clamped
float  illuminanceSphereOrDisk(float  cosTheta , float  sinSigmaSqr) {
    float  sinTheta = sqrt (1.0f - cosTheta * cosTheta);
    float  illuminance = 0.0f;
    // Note: Following  test is  equivalent  to the  original  formula.
    //  There  is 3 phase  in the  curve: cosTheta  > sqrt(sinSigmaSqr),
    //  cosTheta  > -sqrt(sinSigmaSqr) and  else it is 0
    // The  two  outer  case  can be  merge  into a cosTheta * cosTheta  > sinSigmaSqr
    // and  using  saturate(cosTheta) instead.
    if (cosTheta * cosTheta  > sinSigmaSqr) {
        illuminance = PI * sinSigmaSqr * saturate(cosTheta);
    } else {
        float x = sqrt (1.0f / sinSigmaSqr  - 1.0f); // For a disk  this  simplify  to x = d / r
        float y = -x * (cosTheta / sinTheta);
        float  sinThetaSqrtY = sinTheta * sqrt (1.0f - y * y);
        illuminance = (cosTheta * acos(y) - x * sinThetaSqrtY) * sinSigmaSqr + atan(sinThetaSqrtY / x);
    }
    return max(illuminance , 0.0f);
}

float computeSphereLightOpacityFB (
    float3 worldPos, float3 worldNormal, 
    float3 lightPos, float radius
) {
    float3 Lunormalized = lightPos - worldPos;
    float3 L = normalize(Lunormalized);
    float sqrDist = dot(Lunormalized , Lunormalized);

    if (0) {
        // Inigo quilez's solution that doesn't handle horizon
        return PI * saturate(dot(L , worldNormal)) *
        ((radius * radius) / sqrDist);
    } else if (0) {
        // Tilted patch to sphere equation, modified (but still broken like it was in the paper)
        float clampedNormal = dot(worldNormal , L);
        float Beta = acos(clampedNormal);
        float cosBeta = clampedNormal;
        float sinBeta = sqrt(1 - (clampedNormal * clampedNormal));
        float H = sqrt(sqrDist);
        float h = H / radius;
        float x = sqrt(h * h - 1);
        float y = -x * (1 / tan(Beta));

        float illuminance = 0;
        if (h * cosBeta > 1)
            illuminance = cosBeta / (h * h);
        else {
            illuminance = (1 / (PI * h * h)) *
            (cosBeta * acos(y) - x * sinBeta * sqrt(1 - y * y)) +
            (1 / PI) * atan(sinBeta * sqrt(1 - y * y) / x);
        }

        return illuminance * PI;
    } else {
        //  Sphere  evaluation
        float  cosTheta = clamp(dot(worldNormal , L),  -0.999,  0.999); //  Clamp  to avoid  edge  case
        // We need to  prevent  the  object  penetrating  into  the  surface
        // and we must  avoid  divide  by 0, thus  the  0.9999f
        float  sqrLightRadius = radius * radius;
        float  sinSigmaSqr = min(sqrLightRadius / sqrDist , 0.9999f);
        float  illuminance = illuminanceSphereOrDisk(cosTheta , sinSigmaSqr);
        return illuminance;
    }
}
