// Results in /Od look worse so just always /O3
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "SphereLightCore.fxh"

void SphereLightPixelShader (
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

    lightProperties.w *= enableShadows;

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, 
        lightProperties, moreLightProperties
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

void SphereLightWithDistanceRampPixelShader(
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

    lightProperties.w *= enableShadows;

    float opacity = SphereLightPixelCoreWithRamp(
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

technique SphereLight {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightPixelShader();
    }
}

technique SphereLightWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightWithDistanceRampPixelShader();
    }
}