#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.1

float4 ApplyTransform (float3 position) {
    return mul(mul(float4(position.xyz, 1), Viewport.ModelView), Viewport.Projection);
}

void DirectionalLightVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float4 color               : TEXCOORD4,
    inout float3 lightDirection      : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float4 moreLightProperties : TEXCOORD3,
    out float2   worldPosition       : TEXCOORD2,
    out float4   result              : POSITION0
) {
    float2 position = LightCorners[cornerIndex.x] * 99999;
    worldPosition = position;
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, 0));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

void DirectionalLightProbeVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float4 color               : TEXCOORD4,
    inout float3 lightDirection      : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float4 moreLightProperties : TEXCOORD3,
    out float4   result              : POSITION0
) {
    float2 clipPosition = LightCorners[cornerIndex.x] * 99999;

    result = float4(clipPosition.xy, 0, 1);
}

float DirectionalLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 lightDirection      : TEXCOORD0,
    // enableShadows, shadowTraceLength, shadowSoftness, shadowRampRate
    in float4 lightProperties     : TEXCOORD1,
    // aoRadius, shadowDistanceFalloff, shadowRampLength, aoOpacity
    in float4 moreLightProperties : TEXCOORD3,
    in bool   useOpacityRamp,
    in bool   staticShadowsFlag
) {
    float lightOpacity = computeDirectionalLightOpacity(lightDirection, shadedPixelNormal);
    bool visible = (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstantsEx(staticShadowsFlag);

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    computeAO(lightOpacity, shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    bool traceShadows = visible && lightProperties.x && (lightOpacity >= 1 / 256.0);

    // FIXME: Cone trace for directional shadows?
    [branch]
    if (traceShadows) {
        float3 fakeLightCenter = shadedPixelPosition - (lightDirection * lightProperties.y);
        float2 fakeRamp = float2(lightProperties.z, moreLightProperties.y);
        lightOpacity *= coneTrace(
            fakeLightCenter, fakeRamp, 
            float2(lightProperties.w, moreLightProperties.y),
            shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal), 
            vars
        );
    }

    [branch]
    if (useOpacityRamp)
        lightOpacity = SampleFromRamp(lightOpacity);

    [branch]
    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    if (visible) {
        return lightOpacity;
    } else {
        discard;
        return 0;
    }
}

void DirectionalLightPixelShader(
    in  float2 worldPosition       : TEXCOORD2,
    in  float3 lightDirection      : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    bool staticShadowsFlag;
    sampleGBufferEx(
        vpos,
        shadedPixelPosition, shadedPixelNormal,
        staticShadowsFlag
    );

    float opacity = DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightDirection, lightProperties, moreLightProperties, false, staticShadowsFlag
    );

    float4 lightColorActual = float4(color.rgb * color.a * opacity, 1);
    result = lightColorActual;
}

void DirectionalLightWithRampPixelShader(
    in  float2 worldPosition       : TEXCOORD2,
    in  float3 lightDirection      : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    bool staticShadowsFlag;
    sampleGBufferEx(
        vpos,
        shadedPixelPosition, shadedPixelNormal,
        staticShadowsFlag
    );

    float opacity = DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightDirection, lightProperties, moreLightProperties, true, staticShadowsFlag
    );

    float4 lightColorActual = float4(color.rgb * color.a * opacity, 1);
    result = lightColorActual;
}

void DirectionalLightProbePixelShader(
    in  float2 worldPosition       : TEXCOORD2,
    in  float3 lightDirection      : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightDirection, lightProperties, moreLightProperties, false, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void DirectionalLightProbeWithRampPixelShader(
    in  float2 worldPosition       : TEXCOORD2,
    in  float3 lightDirection      : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= DirectionalLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightDirection, lightProperties, moreLightProperties, true, false
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