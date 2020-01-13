// Results in /Od are sometimes incorrect
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "ProjectorLightCore.fxh"

void ProjectorLightPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float4 mat1                : TEXCOORD0,
    in  float4 mat2                : TEXCOORD1,
    in  float4 mat3                : TEXCOORD4,
    in  float4 mat4                : TEXCOORD5,
    // opacity, wrap, texX1, texY1
    in  float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in  float4 moreLightProperties : TEXCOORD3,
    in  float  mipBias             : TEXCOORD6,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    float4 projectorSpacePosition;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = ProjectorLightPixelCore(
        shadedPixelPosition, shadedPixelNormal,
        mat1, mat2, mat3, mat4,
        lightProperties, moreLightProperties,
        projectorSpacePosition
    );

    result = ProjectorLightColorCore(projectorSpacePosition, mipBias, opacity);
}

technique ProjectorLight {
    pass P0
    {
        vertexShader = compile vs_3_0 ProjectorLightVertexShader();
        pixelShader  = compile ps_3_0 ProjectorLightPixelShader();
    }
}
