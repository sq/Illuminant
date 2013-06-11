#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"

uniform float InverseScaleFactor;
uniform float MiddleGray;
uniform float AverageLuminance, MaximumLuminanceSquared;

static const float3 RGBToLuminance = float3(0.299f, 0.587f, 0.114f); 

void GammaCompressedPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
	addColor.rgb *= addColor.a;
	addColor.a = 0;

	result = multiplyColor * (tex2D(TextureSampler, clamp(texCoord, texTL, texBR)) * InverseScaleFactor);
	result += (addColor * result.a);

    float resultLuminance = dot(result, RGBToLuminance);
    float scaledLuminance = (resultLuminance * MiddleGray) / AverageLuminance;
    float compressedLuminance = (scaledLuminance * (1 + (scaledLuminance / MaximumLuminanceSquared))) / (1 + scaledLuminance); 

    result *= compressedLuminance;
}

technique WorldSpaceGammaCompressedBitmap
{
    pass P0
    {
        vertexShader = compile vs_1_1 WorldSpaceVertexShader();
        pixelShader = compile ps_2_0 GammaCompressedPixelShader();
    }
}

technique ScreenSpaceGammaCompressedBitmap
{
    pass P0
    {
        vertexShader = compile vs_1_1 ScreenSpaceVertexShader();
        pixelShader = compile ps_2_0 GammaCompressedPixelShader();
    }
}