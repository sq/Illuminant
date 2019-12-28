// Results in /Od are sometimes incorrect
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "ProjectorLightCore.fxh"

void ProjectorLightProbeVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 color               : TEXCOORD4,
    out float4 result                : POSITION0
) {
    DEFINE_LightCorners
    if (cornerIndex.x > 3) {
        result = float4(-9999, -9999, 0, 0);
    } else {
        float2 clipPosition = (LightCorners[cornerIndex.x] * 9999) - 1;
        result = float4(clipPosition.xy, 0, 1);
    }
}

void ProjectorLightProbePixelShader(
    in  float3 mat1                : TEXCOORD0,
    in  float3 mat2                : TEXCOORD1,
    in  float3 mat3                : TEXCOORD4,
    in  float3 mat4                : TEXCOORD5,
    // radius, ramp length, ramp mode, enable shadows
    in  float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in  float4 moreLightProperties : TEXCOORD3,
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

    opacity *= ProjectorLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, 
        mat1, mat2, mat3, mat4, lightProperties, moreLightProperties
    );

    // FIXME: color
    float4 color = float4(0.8, 0.1, 0.1, 1);

    result = float4(color.rgb * color.a * opacity, 1);
}

technique ProjectorLightProbe {
    pass P0
    {
        vertexShader = compile vs_3_0 ProjectorLightProbeVertexShader();
        pixelShader  = compile ps_3_0 ProjectorLightProbePixelShader();
    }
}