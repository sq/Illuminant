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
    bool  distanceCull = false;

    float4 coneLightProperties = lightProperties;
    float3 lightCenter = computeLightCenter(shadedPixelPosition, startPosition, endPosition, u);

    float distanceOpacity = computeSphereLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, lightProperties, moreLightProperties.z,
        distanceCull
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