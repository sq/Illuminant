#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.1

half SphereLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties     : TEXCOORD1,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in float4 moreLightProperties : TEXCOORD3,
    in bool   useDistanceRamp,
    in bool   useOpacityRamp
) {
    bool  distanceCull = false;
    half distanceOpacity = computeSphereLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, lightProperties, moreLightProperties.z,
        distanceCull
    );

    bool visible = (!distanceCull) && 
        (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    half aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    half preTraceOpacity = distanceOpacity * aoOpacity;

    bool traceShadows = visible && lightProperties.w && (preTraceOpacity >= 1 / 256.0);
    half coneOpacity = 1;

    [branch]
    if (traceShadows) {
        coneOpacity = coneTrace(
            lightCenter, lightProperties.xy, 
            float2(getConeGrowthFactor(), moreLightProperties.y),
            shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
            vars
        );
    }

    half lightOpacity;

    [branch]
    if (useOpacityRamp || useDistanceRamp) {
        half rampInput = useOpacityRamp 
            ? preTraceOpacity * coneOpacity
            : preTraceOpacity;
        half rampResult = SampleFromRamp(rampInput);
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