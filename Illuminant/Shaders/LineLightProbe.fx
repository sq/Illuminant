#include "SphereLightCore.fxh"

void LineLightProbeVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 color               : TEXCOORD4,
    out float4 result                : POSITION0
) {
    if (cornerIndex.x > 3) {
        result = float4(-9999, -9999, 0, 0);
    } else {
        float2 clipPosition = (LightCorners[cornerIndex.x] * 9999) - 1;
        result = float4(clipPosition.xy, 0, 1);
    }
}

void LineLightProbePixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 __vpos__                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float opacity, enableShadows;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity, enableShadows
    );

    lightProperties.w *= enableShadows;
    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, false, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void LineLightProbeWithDistanceRampPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 __vpos__                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float opacity, enableShadows;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity, enableShadows
    );

    lightProperties.w *= enableShadows;
    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, true, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void LineLightProbeWithOpacityRampPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 __vpos__                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float opacity, enableShadows;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity, enableShadows
    );

    lightProperties.w *= enableShadows;
    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, false, true
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

technique LineLightProbe {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightProbeVertexShader();
        pixelShader  = compile ps_3_0 LineLightProbePixelShader();
    }
}

technique LineLightProbeWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightProbeVertexShader();
        pixelShader  = compile ps_3_0 LineLightProbeWithDistanceRampPixelShader();
    }
}

technique LineLightProbeWithOpacityRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightProbeVertexShader();
        pixelShader  = compile ps_3_0 LineLightProbeWithOpacityRampPixelShader();
    }
}