#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"
#include "HDR.fxh"

uniform const float InverseScaleFactor;

void GammaCompressedPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * (tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw)) * InverseScaleFactor);
    result += (addColor * result.a);

    result = GammaCompress(result);
}

void ToneMappedPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    result = multiplyColor * (tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw)) * InverseScaleFactor);
    result += (addColor * result.a);

    float3 preToneMap = max(result.rgb + Offset, 0) * (ExposureMinusOne + 1);

    result = float4(Uncharted2Tonemap(preToneMap) / Uncharted2Tonemap1(WhitePoint), result.a);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
}

technique WorldSpaceGammaCompressedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedPixelShader();
    }
}

technique ScreenSpaceGammaCompressedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedPixelShader();
    }
}

technique WorldSpaceToneMappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 ToneMappedPixelShader();
    }
}

technique ScreenSpaceToneMappedBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 ToneMappedPixelShader();
    }
}