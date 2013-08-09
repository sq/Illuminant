#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"

#include "RampCommon.fxh"

static const float3 RGBToLuminance = float3(0.299f, 0.587f, 0.114f);

void RampPixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
	addColor.rgb *= addColor.a;
	addColor.a = 0;

    result = tex2D(TextureSampler, clamp(texCoord, texTL, texBR));
    float luminance = dot(result, RGBToLuminance);
    float rampFactor = RampLookup(luminance) / luminance;

	result = (multiplyColor * result) * rampFactor;
	result += (addColor * result.a);
}

technique WorldSpaceRampBitmap
{
    pass P0
    {
        vertexShader = compile vs_1_1 WorldSpaceVertexShader();
        pixelShader = compile ps_2_0 RampPixelShader();
    }
}

technique ScreenSpaceRampBitmap
{
    pass P0
    {
        vertexShader = compile vs_1_1 ScreenSpaceVertexShader();
        pixelShader = compile ps_2_0 RampPixelShader();
    }
}