// Results in /Od are sometimes incorrect
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "ProjectorLightCore.fxh"

void ProjectorLightPixelShader(
    in  float3 worldPosition       : POSITION1,
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
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = ProjectorLightPixelCore(
        shadedPixelPosition, shadedPixelNormal,
        mat1, mat2, mat3, mat4,
        lightProperties, moreLightProperties
    );

    // FIXME: color
    float4 color = float4(0.8, 0.1, 0.1, 1);
    result = float4(color.rgb * color.a * opacity, 1);
}

technique ProjectorLight {
    pass P0
    {
        vertexShader = compile vs_3_0 ProjectorLightVertexShader();
        pixelShader  = compile ps_3_0 ProjectorLightPixelShader();
    }
}
