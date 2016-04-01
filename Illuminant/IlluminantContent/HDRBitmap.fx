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
    float rescaleFactor = compressedLuminance / resultLuminance;

    result = result * rescaleFactor;
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


// http://frictionalgames.blogspot.com/2012/09/tech-feature-hdr-lightning.html

uniform float Exposure;
uniform float WhitePoint;

static const float kA = 0.15;
static const float kB = 0.50;
static const float kC = 0.10;
static const float kD = 0.20;
static const float kE = 0.02;
static const float kF = 0.30;

float Uncharted2Tonemap1 (float value)
{
    return (
        (value * (kA * value + kC * kB) + kD * kE) / 
        (value * (kA * value + kB ) + kD * kF)
    ) - kE / kF;
}

float3 Uncharted2Tonemap (float3 rgb)
{
    return (
        (rgb * (kA * rgb + kC * kB) + kD * kE) / 
        (rgb * (kA * rgb + kB ) + kD * kF)
    ) - kE / kF;
}

void ToneMappedPixelShader(
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

    result = float4(Uncharted2Tonemap(result.rgb * Exposure) / Uncharted2Tonemap1(WhitePoint), result.a);
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