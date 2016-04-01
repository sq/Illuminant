#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

void LightingResolvePixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    in float2 vpos  : VPOS,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    result = float4(shadedPixelPosition / 1024, 1);

    /*
    result = multiplyColor * tex2D(TextureSampler, clamp(texCoord, texTL, texBR));
    result += (addColor * result.a);
    */
}

technique LightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolvePixelShader();
    }
}
