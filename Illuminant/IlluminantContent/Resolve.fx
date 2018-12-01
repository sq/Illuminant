#define ENABLE_DITHERING

#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\DitherCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\LUTCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "HDR.fxh"

const float3 RgbToGray = float3(0.299, 0.587, 0.144);

Texture2D DarkLUT : register(t4);
Texture2D BrightLUT : register(t5);
uniform const float2 LUTResolutions;
uniform const float3 LUTLevels;
uniform const bool   PerChannelLUT, LUTOnly;

sampler DarkLUTSampler : register(s4) {
    Texture = (DarkLUT);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

sampler BrightLUTSampler : register(s5) {
    Texture = (BrightLUT);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

sampler LinearSampler : register(s6) {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

sampler PointSampler : register(s7) {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

uniform float2 LightmapUVOffset;
uniform bool   ResolveToSRGB;
uniform float  InverseScaleFactor;

float4 ResolveCommon (
    in float2 texCoord,
    in float4 texRgn
) {
    float4 result;

    float4 coord = float4(clamp(texCoord + LightmapUVOffset, texRgn.xy, texRgn.zw), 0, 0);

    float4 sampleLinear = tex2Dlod(LinearSampler, coord);

    result = sampleLinear;
    result *= InverseScaleFactor;
    result.a = 1;
    return result;
}

float4 ResolveWithAlbedoCommon (
    in float2 texCoord1,
    in float4 texRgn1,
    in float2 texCoord2,
    in float4 texRgn2
) {
    texCoord1 = clamp(texCoord1, texRgn1.xy, texRgn1.zw);
    texCoord2 = clamp(texCoord2 + LightmapUVOffset, texRgn2.xy, texRgn2.zw);

    float4 light = tex2Dlod(TextureSampler2, float4(texCoord2, 0, 0));
    float4 albedo = tex2Dlod(TextureSampler, float4(texCoord1, 0, 0));

    light *= InverseScaleFactor * 2;

    float4 result = albedo;
    result.rgb *= light.rgb;
    return result;
}

float4 LUTBlendedResolveWithAlbedoCommon(
    in float2 texCoord1,
    in float4 texRgn1,
    in float2 texCoord2,
    in float4 texRgn2
) {
    texCoord1 = clamp(texCoord1, texRgn1.xy, texRgn1.zw);
    texCoord2 = clamp(texCoord2 + LightmapUVOffset, texRgn2.xy, texRgn2.zw);

    float4 light = tex2Dlod(TextureSampler2, float4(texCoord2, 0, 0));
    float4 albedo = tex2Dlod(TextureSampler, float4(texCoord1, 0, 0));

    light *= InverseScaleFactor * 2;

    float3 weight = light.rgb;
    float bandWidth = saturate(LUTLevels.z - LUTLevels.x);
    float neutralBandWidth = (LUTLevels.y * bandWidth);
    bool hasNeutralBand = (neutralBandWidth >= 0.001);
    bool normalize = !PerChannelLUT || hasNeutralBand;
    if (normalize) {
        weight *= RgbToGray;
        weight = weight.r + weight.g + weight.b;
    }

    float3 lutValue1 = ReadLUT(DarkLUTSampler, LUTResolutions.x, albedo.rgb);
    float3 lutValue2 = ReadLUT(BrightLUTSampler, LUTResolutions.y, albedo.rgb);

    float3 blendedValue;
    if (hasNeutralBand) {
        float transitionSize = (bandWidth - neutralBandWidth) * 0.5;
        float v = weight.x - LUTLevels.x, v2 = v - transitionSize, v3 = v2 - neutralBandWidth;

        float3 val1 = lerp(lutValue1, albedo.rgb, saturate(v / transitionSize));
        blendedValue = lerp(val1, lutValue2, saturate(v3 / transitionSize));
    } else {
        if (LUTLevels.z > LUTLevels.x) {
            weight -= LUTLevels.x;
            weight = max(0, weight);
            weight /= (LUTLevels.z - LUTLevels.x);
            weight = saturate(weight);
        } else {
            // HACK
            weight -= LUTLevels.x;
            weight = saturate(weight);
        }
        blendedValue = lerp(lutValue1, lutValue2, weight);
    }

    float4 result = float4(blendedValue * (LUTOnly ? 1 : light.rgb), albedo.a);
    return result;
}

void LightingResolvePixelShader (
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveCommon(texCoord1, texRgn1);

    result.rgb = max(0, result.rgb + Offset);
    result.rgb *= (ExposureMinusOne + 1);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void GammaCompressedLightingResolvePixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveCommon(texCoord1, texRgn1);

    result = GammaCompress(result);
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void ToneMappedLightingResolvePixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveCommon(texCoord1, texRgn1);

    float3 preToneMap = max(0, result.rgb + Offset) * (ExposureMinusOne + 1);

    result = float4(Uncharted2Tonemap(preToneMap) / Uncharted2Tonemap1(WhitePoint), result.a);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void LightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    result.rgb = max(0, result.rgb + Offset);
    result.rgb *= (ExposureMinusOne + 1);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void LUTBlendedLightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = LUTBlendedResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    result.rgb = max(0, result.rgb + Offset);
    result.rgb *= (ExposureMinusOne + 1);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void GammaCompressedLightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    result = GammaCompress(result);
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void ToneMappedLightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    float3 preToneMap = max(0, result.rgb + Offset) * (ExposureMinusOne + 1);

    result = float4(Uncharted2Tonemap(preToneMap) / Uncharted2Tonemap1(WhitePoint), result.a);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void CalculateLuminancePixelShader(
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float4 coord = float4(clamp(texCoord1, texRgn1.xy, texRgn1.zw), 0, 0);
    float2 coordTexels = coord.xy * BitmapTextureSize;

    float4 samplePoint = tex2Dlod(PointSampler, coord);

    float3 rgbScaled = samplePoint.rgb * RgbToGray;
    float luminance = (rgbScaled.r + rgbScaled.g + rgbScaled.b);

    result = float4(luminance, luminance, luminance, luminance);
}

technique ScreenSpaceLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolvePixelShader();
    }
}

technique ScreenSpaceGammaCompressedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedLightingResolvePixelShader();
    }
}

technique ScreenSpaceToneMappedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 ToneMappedLightingResolvePixelShader();
    }
}

technique ScreenSpaceLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolveWithAlbedoPixelShader();
    }
}

technique ScreenSpaceLUTBlendedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LUTBlendedLightingResolveWithAlbedoPixelShader();
    }
}

technique ScreenSpaceGammaCompressedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedLightingResolveWithAlbedoPixelShader();
    }
}

technique ScreenSpaceToneMappedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 ToneMappedLightingResolveWithAlbedoPixelShader();
    }
}

technique WorldSpaceLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolvePixelShader();
    }
}

technique WorldSpaceGammaCompressedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedLightingResolvePixelShader();
    }
}

technique WorldSpaceToneMappedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 ToneMappedLightingResolvePixelShader();
    }
}

technique WorldSpaceLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolveWithAlbedoPixelShader();
    }
}

technique WorldSpaceGammaCompressedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedLightingResolveWithAlbedoPixelShader();
    }
}

technique WorldSpaceToneMappedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 ToneMappedLightingResolveWithAlbedoPixelShader();
    }
}

technique WorldSpaceLUTBlendedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LUTBlendedLightingResolveWithAlbedoPixelShader();
    }
}

technique CalculateLuminance
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 CalculateLuminancePixelShader();
    }
}