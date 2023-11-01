// Results in /Od are sometimes incorrect
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "VolumetricLightCore.fxh"

void VolumetricLightPixelShader(
    in  float3 worldPosition       : POSITION1,
    // xyz + radius
    in  float4 startPosition       : TEXCOORD0,
    // xyz + radius
    in  float4 endPosition         : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float4 specular            : TEXCOORD5,
    in  float4 rayNormal           : TEXCOORD6,
    in  float4 evenMoreLightProperties : TEXCOORD7,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition, shadedPixelNormal;
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

    lightProperties.w *= enableShadows;

    float opacity = VolumetricLightPixelCore(
        shadedPixelPosition, shadedPixelNormal,
        startPosition, endPosition, rayNormal,
        lightProperties, moreLightProperties,
        evenMoreLightProperties, GET_VPOS
    );
    if (opacity <= 0)
    {
        result = 0;
        discard;
    }

    result = float4(color.rgb * color.a * opacity, 1);
}

technique VolumetricLight {
    pass P0
    {
        vertexShader = compile vs_3_0 VolumetricLightVertexShader();
        pixelShader  = compile ps_3_0 VolumetricLightPixelShader();
    }
}
