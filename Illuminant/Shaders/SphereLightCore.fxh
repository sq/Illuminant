#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"
#include "FBPBR.fxh"

#define SELF_OCCLUSION_HACK 1.6
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)

void SphereLightVertexShader(
    in int2 vertexIndex              : BLENDINDICES0,
    inout float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 color               : TEXCOORD4,
    out float3 worldPosition         : POSITION1,
    out float4 result                : POSITION0
) {
    DEFINE_ClippedLightVertices
    float3 vertex = ClippedLightVertices[vertexIndex.x];

    float  radius = lightProperties.x + lightProperties.y + 1;
    float  deltaY = (radius) - (radius / moreLightProperties.z);
    float3 radius3;

    if (1)
        // HACK: Scale the y axis some to clip off dead pixels caused by the y falloff factor
        radius3 = float3(radius, radius - (deltaY / 2.0), 0);
    else
        radius3 = float3(radius, radius, 0);

    float3 tl = lightCenter - radius3, br = lightCenter + radius3;

    // Unfortunately we need to adjust both by the light's radius (to account for pixels above/below the center point
    //  being lit in 2.5d projection), along with adjusting by the z of the light's centerpoint (to deal with pixels
    //  at high elevation)
    float radiusOffset = radius * getInvZToYMultiplier();
    float zOffset = lightCenter.z * getZToYMultiplier();

    worldPosition = lerp(tl, br, vertex);
    if (vertex.y < 0.5) {
        worldPosition.y -= radiusOffset;
        worldPosition.y -= zOffset;
    }

    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float SphereLightPixelPrologue (
    inout float3 shadedPixelPosition,
    inout float3 shadedPixelNormal,
    inout float3 lightCenter,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties,
    out   bool   visible
) {
    float distanceOpacity = computeSphereLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, lightProperties, moreLightProperties.z
    );

    visible = (distanceOpacity > 0) && 
        (shadedPixelPosition.x > -9999);

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    return distanceOpacity;
}

float SphereLightPixelEpilogue (
    in float preTraceOpacity, 
    in float coneOpacity,
    in bool  visible,
    in bool  useDistanceRamp
) {
    float lightOpacity = preTraceOpacity;

    PREFER_BRANCH
    if (useDistanceRamp)
        lightOpacity = SampleFromRamp(preTraceOpacity);

    lightOpacity *= coneOpacity;

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}

float SphereLightPixelCore (
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 lightCenter,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in float4 moreLightProperties,
    in bool   useDistanceRamp
) {
    bool visible;
    float distanceOpacity = SphereLightPixelPrologue(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, lightProperties,
        moreLightProperties, visible
    );

    clip(visible ? 1 : -1);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);
    float preTraceOpacity = distanceOpacity * aoOpacity;

    bool traceShadows = visible && lightProperties.w && (preTraceOpacity >= SHADOW_OPACITY_THRESHOLD);
    float coneOpacity = coneTrace(
        lightCenter, lightProperties.xy, 
        float2(getConeGrowthFactor(), moreLightProperties.y),
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
        vars, traceShadows
    );

    return SphereLightPixelEpilogue(
        preTraceOpacity, coneOpacity, visible,
        useDistanceRamp
    );
}

float SphereLightPixelCoreNoDF (
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 lightCenter,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in float4 moreLightProperties,
    in bool   useDistanceRamp
) {
    bool visible;
    float distanceOpacity = SphereLightPixelPrologue(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, lightProperties,
        moreLightProperties, visible
    );

    return SphereLightPixelEpilogue(
        distanceOpacity, 1, visible,
        useDistanceRamp
    );
}