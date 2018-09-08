#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)

float3 computeLightCenter (float3 worldPosition, float3 startPosition, float3 endPosition, out float u) {
    return closestPointOnLineSegment3(startPosition, endPosition, worldPosition, u);
}

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
    float  rectHalfSize = float2(halfWidth , halfHeight);
    dist2D = clamp(dist2D , -rectHalfSize , rectHalfSize);
    return  planeOrigin + dist2D.x * left + dist2D.y * up;
}

float  rightPyramidSolidAngle(float dist , float  halfWidth , float  halfHeight)
{
float a = halfWidth;
float b = halfHeight;
float h = dist;
return 4 * asin(a * b / sqrt((a * a + h * h) * (b * b + h * h)));
}

float  rectangleSolidAngle(float3  worldPos ,
float3 p0, float3 p1,
float3 p2, float3  p3)
{
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
    out bool distanceFalloff, out float3 spherePosition, out float u
) {
    distanceFalloff = false;

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
    float3  up         = cross(lightLeft , forward);
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
    (( lightRadius * lightRadius) / sqrSphereDistance);

    // HACK: The paper did a + here instead but that produces oddly shaped lobes at the ends and the center
    if (1)
        illuminance  = max(illuminance, illuminanceSphere);
    else
        illuminance = illuminance + illuminanceSphere;

    // If you remove the saturate this overbrightens in a really sick way
    if (1)
        return saturate(illuminance);
    else
        return illuminance;
}

float LineLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 startPosition,
    in float3 endPosition,
    out float u,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in float4 moreLightProperties,
    in bool   useDistanceRamp,
    in bool   useOpacityRamp
) {
    float4 coneLightProperties = lightProperties;

    bool  distanceCull;
    float3 lightCenter;
    float distanceOpacity = computeLineLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        startPosition, endPosition, lightProperties, 
        distanceCull, lightCenter, u
    );

    bool visible = (!distanceCull) && 
        (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    float preTraceOpacity = distanceOpacity * aoOpacity;

    bool traceShadows = visible && lightProperties.w && (preTraceOpacity >= SHADOW_OPACITY_THRESHOLD);
    float coneOpacity = 1;

    coneOpacity = coneTrace(
        lightCenter, coneLightProperties.xy, 
        float2(getConeGrowthFactor(), moreLightProperties.y),
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
        vars, true, traceShadows
    );

    float lightOpacity;

    [branch]
    if (useOpacityRamp || useDistanceRamp) {
        float rampInput = useOpacityRamp 
            ? preTraceOpacity * coneOpacity
            : preTraceOpacity;
        float rampResult = SampleFromRamp(rampInput);
        lightOpacity = useOpacityRamp
            ? rampResult
            : rampResult * coneOpacity;
    } else {
        lightOpacity = preTraceOpacity * coneOpacity;
    }

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}