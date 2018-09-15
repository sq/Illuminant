#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"
#include "FBPBR.fxh"

#define SELF_OCCLUSION_HACK 1.5
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)

float3 computeLightCenter (float3 worldPosition, float3 startPosition, float3 endPosition, out float u) {
    return closestPointOnLineSegment3(startPosition, endPosition, worldPosition, u);
}

float lineConeTrace (
    in float3 startPosition,
    in float3 endPosition,
    in float u,
    in float2 lightRamp,
    in float2 coneGrowthFactorAndDistanceFalloff,
    in float3 shadedPixelPosition,
    in DistanceFieldConstants vars,
    in bool   enable
) {
    TraceState a, b, c;
    float3 delta = endPosition - startPosition;
    float deltaLength = length(delta);
    float offset = max(saturate((lightRamp.x + 1) / deltaLength), 0.03);

    coneTraceInitialize(a, shadedPixelPosition, startPosition + saturate(u - offset) * delta, TRACE_INITIAL_OFFSET_PX, lightRamp.x, false);
    coneTraceInitialize(b, shadedPixelPosition, startPosition + u * delta, TRACE_INITIAL_OFFSET_PX, lightRamp.x, false);
    coneTraceInitialize(c, shadedPixelPosition, startPosition + saturate(u + offset) * delta, TRACE_INITIAL_OFFSET_PX, lightRamp.x, false);

    float4 config = createTraceConfig(lightRamp, coneGrowthFactorAndDistanceFalloff);

    float stepsRemaining = getStepLimit();
    float liveness = (DistanceField.Extent.x > 0) && enable;

    [loop]
    while (liveness > 0) {
        float stepLiveness =
            coneTraceAdvanceEx(a, config, vars) +
            coneTraceAdvanceEx(b, config, vars) +
            coneTraceAdvanceEx(c, config, vars);

        stepsRemaining--;
        liveness = stepsRemaining * stepLiveness;
    }

    // HACK: Force visibility down to 0 if we are going to terminate the trace because we took too many steps.
    float stepWindowVisibility = stepsRemaining / MAX_STEP_RAMP_WINDOW;
    float visibility = min(
        (coneTraceFinish(a) + coneTraceFinish(b) + coneTraceFinish(c)) / 3,
        stepWindowVisibility
    );

    float finalResult = pow(
        saturate(
            saturate((visibility - FULLY_SHADOWED_THRESHOLD)) /
            (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD)
        ),
        getOcclusionToOpacityPower()
    );

    return enable ? finalResult : 1.0;
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

    float3 lightCenter;
    float distanceOpacity = computeLineLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        startPosition, endPosition, lightProperties, 
        lightCenter, u
    );

    bool visible = (distanceOpacity > 0) && 
        (shadedPixelPosition.x > -9999);

    clip(visible ? 1 : -1);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    float preTraceOpacity = distanceOpacity * aoOpacity;

    bool traceShadows = visible && lightProperties.w && (preTraceOpacity >= SHADOW_OPACITY_THRESHOLD);
    float coneOpacity = lineConeTrace(
        startPosition, endPosition, u,
        coneLightProperties.xy, 
        float2(getConeGrowthFactor(), moreLightProperties.y),
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
        vars, traceShadows
    );

    float lightOpacity = preTraceOpacity;
    lightOpacity *= coneOpacity;

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}