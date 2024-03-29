#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "SphereLightCore.fxh"

void SphereLightWithoutDistanceFieldPixelShader (
    in  float3 worldPosition       : POSITION1,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float4 specular            : TEXCOORD5,
    in  float4 evenMoreLightProperties : TEXCOORD7,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition, shadedPixelNormal;
    bool enableShadows, fullbright;
    float3 cameraPosition = sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, enableShadows, fullbright
    );

    if (fullbright || checkShadowFilter(evenMoreLightProperties, enableShadows)) {
        result = 0;
        discard;
        return;
    }

    float opacity = SphereLightPixelCoreNoDF(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties
    );
    float specularity = CalcSphereLightSpecularity(
        cameraPosition, shadedPixelPosition, shadedPixelNormal, lightCenter,
        specular.a
    );

    result = float4(
        (color.rgb * color.a * opacity) +
        (specular.rgb * specularity * opacity), 1
    );
}

technique SphereLightWithoutDistanceField {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader = compile ps_3_0 SphereLightWithoutDistanceFieldPixelShader();
    }
}