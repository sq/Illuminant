// Results in /Od are sometimes incorrect
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "ProjectorLightCore.fxh"

sampler ProjectorTextureSampler : register(s5) {
    Texture = (RampTexture);
    AddressU = WRAP;
    AddressV = WRAP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void ProjectorLightPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float4 mat1                : TEXCOORD0,
    in  float4 mat2                : TEXCOORD1,
    in  float4 mat3                : TEXCOORD4,
    in  float4 mat4                : TEXCOORD5,
    // radius, ramp length, ramp mode, enable shadows
    in  float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in  float4 moreLightProperties : TEXCOORD3,
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

    projectorSpacePosition.z = 0;
    projectorSpacePosition.w = 0;
    float4 texColor = tex2Dlod(ProjectorTextureSampler, projectorSpacePosition);

    // FIXME: color
    float4 color = texColor * float4(0.33, 0.33, 0.33, 1);
    result = float4(color.rgb * color.a * opacity, 1);
}

technique ProjectorLight {
    pass P0
    {
        vertexShader = compile vs_3_0 ProjectorLightVertexShader();
        pixelShader  = compile ps_3_0 ProjectorLightPixelShader();
    }
}
