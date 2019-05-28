#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5

float4 ApplyTransform (float3 position) {
    return mul(mul(float4(position.xyz, 1), Viewport.ModelView), Viewport.Projection);
}

void DirectionalLightVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float3 lightPositionMin    : TEXCOORD0,
    inout float3 lightPositionMax    : TEXCOORD1,
    inout float4 lightProperties     : TEXCOORD2,
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 color               : TEXCOORD4,
    inout float4 lightDirection      : TEXCOORD5,
    out float3   worldPosition       : POSITION1,
    out float4   result              : POSITION0
) {
    // FIXME: Z
    DEFINE_LightCorners
    worldPosition = lerp(lightPositionMin, lightPositionMax, LightCorners[cornerIndex.x]);
    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

void DirectionalLightProbeVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float4 lightDirection      : TEXCOORD5,
    inout float4 lightProperties     : TEXCOORD2,
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 color               : TEXCOORD4,
    out float4   result              : POSITION0
) {
    if (cornerIndex.x > 3) {
        result = float4(-9999, -9999, 0, 0);
    } else {
        DEFINE_LightCorners
        float2 clipPosition = (LightCorners[cornerIndex.x] * 99999) - 1;
        result = float4(clipPosition.xy, 0, 1);
    }
}

float DirectionalLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float4 lightDirection,
    // enableShadows, shadowTraceLength, shadowSoftness, shadowRampRate
    in float4 lightProperties,
    // aoRadius, shadowDistanceFalloff, shadowRampLength, aoOpacity
    in float4 moreLightProperties,
    in bool   useOpacityRamp
) {
    float lightOpacity = computeDirectionalLightOpacity(lightDirection, shadedPixelNormal);
    bool visible = (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);
    lightOpacity *= aoOpacity;

    bool traceShadows = visible && lightProperties.x && (lightOpacity >= 1 / 256.0) && (lightDirection.w >= 0.1);

    // FIXME: Cone trace for directional shadows?
    float3 fakeLightCenter = shadedPixelPosition - (lightDirection.xyz * lightProperties.y);
    float2 fakeRamp = float2(lightProperties.z, moreLightProperties.y);
    lightOpacity *= coneTrace(
        fakeLightCenter, fakeRamp, 
        float2(lightProperties.w, moreLightProperties.y),
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal), 
        vars, traceShadows
    );

    [branch]
    if (useOpacityRamp)
        lightOpacity = SampleFromRamp(lightOpacity);

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}

void DirectionalLightPixelShader(
    in  float2 worldPosition       : POSITION1,
    in  float4 lightDirection      : TEXCOORD5,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightDirection, lightProperties, moreLightProperties, false
    );

    float4 lightColorActual = float4(color.rgb * color.a * opacity, 1);
    result = lightColorActual;
}

void DirectionalLightWithRampPixelShader(
    in  float2 worldPosition       : POSITION1,
    in  float4 lightDirection      : TEXCOORD5,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightDirection, lightProperties, moreLightProperties, true
    );

    float4 lightColorActual = float4(color.rgb * color.a * opacity, 1);
    result = lightColorActual;
}

void DirectionalLightProbePixelShader(
    in  float4 lightDirection      : TEXCOORD5,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float opacity, enableShadows;

    // FIXME: Clip to region

    sampleLightProbeBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, opacity, enableShadows
    );

    lightProperties.x *= enableShadows;
    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightDirection, lightProperties, moreLightProperties, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void DirectionalLightProbeWithRampPixelShader(
    in  float4 lightDirection      : TEXCOORD5,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float opacity, enableShadows;

    // FIXME: Clip to region

    sampleLightProbeBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, opacity, enableShadows
    );

    lightProperties.x *= enableShadows;
    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightDirection, lightProperties, moreLightProperties, true
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

technique DirectionalLight {
    pass P0
    {
        vertexShader = compile vs_3_0 DirectionalLightVertexShader();
        pixelShader  = compile ps_3_0 DirectionalLightPixelShader();
    }
}

technique DirectionalLightWithRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 DirectionalLightVertexShader();
        pixelShader  = compile ps_3_0 DirectionalLightWithRampPixelShader();
    }
}

technique DirectionalLightProbe {
    pass P0
    {
        vertexShader = compile vs_3_0 DirectionalLightProbeVertexShader();
        pixelShader = compile ps_3_0 DirectionalLightProbePixelShader();
    }
}

technique DirectionalLightProbeWithRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 DirectionalLightProbeVertexShader();
        pixelShader = compile ps_3_0 DirectionalLightProbeWithRampPixelShader();
    }
}