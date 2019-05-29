#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "LineLightCore.fxh"

void LineLightWithDistanceRampPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 startPosition       : TEXCOORD0,
    in  float3 endPosition         : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 startColor          : TEXCOORD4,
    in  float4 endColor            : TEXCOORD5,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float u;
    float opacity = LineLightPixelCore(
        shadedPixelPosition, shadedPixelNormal,
        startPosition, endPosition, u,
        lightProperties, moreLightProperties, true, false
    );

    float4 color = lerp(startColor, endColor, u);
    result = float4(color.rgb * color.a * opacity, 1);
}

void LineLightWithOpacityRampPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 startPosition       : TEXCOORD0,
    in  float3 endPosition         : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 startColor          : TEXCOORD4,
    in  float4 endColor            : TEXCOORD5,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float u;
    float opacity = LineLightPixelCore(
        shadedPixelPosition, shadedPixelNormal,
        startPosition, endPosition, u,
        lightProperties, moreLightProperties, false, true
    );

    float4 color = lerp(startColor, endColor, u);
    result = float4(color.rgb * color.a * opacity, 1);
}

technique LineLightWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightVertexShader();
        pixelShader  = compile ps_3_0 LineLightWithDistanceRampPixelShader();
    }
}

technique LineLightWithOpacityRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightVertexShader();
        pixelShader  = compile ps_3_0 LineLightWithOpacityRampPixelShader();
    }
}