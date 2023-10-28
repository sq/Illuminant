#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "SphereLightCore.fxh"

void SphereLightProbeVertexShader(
    in float3 cornerWeights          : NORMAL2,
    inout float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 color               : TEXCOORD4,
    inout float4 evenMoreLightProperties : TEXCOORD7,
    out float4 result                : POSITION0
) {
    float2 clipPosition = (cornerWeights.xy * 99999) - 1;
    result = float4(clipPosition.xy, 0, 1);
}

void SphereLightProbePixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float opacity, enableShadows;

    sampleLightProbeBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, opacity, enableShadows
    );

    lightProperties.w *= enableShadows;
    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightProbeWithDistanceRampPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float opacity, enableShadows;

    sampleLightProbeBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, opacity, enableShadows
    );

    lightProperties.w *= enableShadows;
    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCoreWithRamp(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

technique SphereLightProbe {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbePixelShader();
    }
}

technique SphereLightProbeWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbeWithDistanceRampPixelShader();
    }
}