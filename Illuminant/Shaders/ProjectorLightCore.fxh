#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)

float ProjectorLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 mat1, 
    in float3 mat2, 
    in float3 mat3, 
    in float3 mat4,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in float4 moreLightProperties
) {
    float4 coneLightProperties = lightProperties;

    // FIXME: Projector falloff/clamping?
    float distanceOpacity = 1;

    bool visible = (distanceOpacity > 0) && 
        (shadedPixelPosition.x > -9999);

    clip(visible ? 1 : -1);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    float preTraceOpacity = distanceOpacity * aoOpacity;

    // FIXME: Projector shadows?
    /*
    bool traceShadows = visible && lightProperties.w && (preTraceOpacity >= SHADOW_OPACITY_THRESHOLD);
    float coneOpacity = lineConeTrace(
        startPosition, endPosition, u,
        coneLightProperties.xy, 
        float2(getConeGrowthFactor(), moreLightProperties.y),
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
        vars, traceShadows
    );
    */
    float coneOpacity = 1;

    float lightOpacity = preTraceOpacity;
    lightOpacity *= coneOpacity;

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}

void ProjectorLightVertexShader(
    in int2 vertexIndex              : BLENDINDICES0,
    inout float3 mat1                : TEXCOORD0, 
    inout float3 mat2                : TEXCOORD1, 
    inout float3 mat3                : TEXCOORD4, 
    inout float3 mat4                : TEXCOORD5,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    out float3 worldPosition         : POSITION1,
    out float4 result                : POSITION0
) {
    DEFINE_LightCorners

    float3 vertex = LightCorners[vertexIndex.x];
    // FIXME: Projector bounding box
    float3 tl = -9999, br = 9999;

    worldPosition = lerp(tl, br, vertex);

    // FIXME: z offset

    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}
