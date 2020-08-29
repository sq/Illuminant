// Results in /Od look worse so just always /O3
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "LightCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5

float4 ApplyTransform (float3 position) {
    return mul(mul(float4(position.xyz, 1), Viewport.ModelView), Viewport.Projection);
}

void DirectionalLightVertexShader(
    in    float3 cornerWeights       : NORMAL2,
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
    worldPosition = lerp(lightPositionMin, lightPositionMax, cornerWeights.xyz);
    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float DirectionalLightPixelCoreNoDF(
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

    moreLightProperties.x *= 0;

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}

void DirectionalLightPixelShaderNoDF(
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
    bool enableShadows, fullbright;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, enableShadows, fullbright
    );

    if (fullbright) {
        result = 0;
        discard;
        return;
    }

    enableShadows = 0;
    lightProperties.x *= enableShadows;

    float opacity = DirectionalLightPixelCoreNoDF(
        shadedPixelPosition, shadedPixelNormal, lightDirection, lightProperties, moreLightProperties, false
    );

    float4 lightColorActual = float4(color.rgb * color.a * opacity, 1);
    result = lightColorActual;
}

technique DirectionalLightWithoutDistanceField {
    pass P0
    {
        vertexShader = compile vs_3_0 DirectionalLightVertexShader();
        pixelShader  = compile ps_3_0 DirectionalLightPixelShaderNoDF();
    }
}