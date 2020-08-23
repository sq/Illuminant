#define ENABLE_DITHERING

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\sRGBCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\DitherCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "HDR.fxh"

const float3 RgbToGray = float3(0.299, 0.587, 0.144);

sampler PointSampler : register(s7) {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

uniform float2 LightmapUVOffset;
uniform float  ResolveToSRGB;
uniform float  InverseScaleFactor;

float4 ResolveCommon (
    in float2 texCoord,
    in float4 texRgn
) {
    float4 result;

    float4 coord = float4(clamp2(texCoord + LightmapUVOffset, texRgn.xy, texRgn.zw), 0, 0);

    float4 color = tex2Dlod(TextureSampler, coord);

    result = color * InverseScaleFactor;
    result.a = 1;
    return result;
}

float4 ResolveWithAlbedoCommon (
    in float2 texCoord1,
    in float4 texRgn1,
    in float2 texCoord2,
    in float4 texRgn2
) {
    texCoord1 = clamp2(texCoord1, texRgn1.xy, texRgn1.zw);
    texCoord2 = clamp2(texCoord2 + LightmapUVOffset, texRgn2.xy, texRgn2.zw);

    float4 light = tex2Dlod(TextureSampler2, float4(texCoord2, 0, 0));
    float4 albedo = tex2Dlod(TextureSampler, float4(texCoord1, 0, 0));
    if (ResolveToSRGB)
        albedo = pSRGBToPLinear(albedo);

    light *= InverseScaleFactor * 2;

    float4 result = float4(
        lerp(albedo.rgb, albedo.rgb * light.rgb, light.a), albedo.a
    );
    return result;
}

void LightingResolvePixelShader (
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    if (0) {
        float2 vpos = GET_VPOS / GetRenderTargetSize();
        result = float4(vpos.x, vpos.y, 0, 1);
        return;
    }

    result = ResolveCommon(texCoord1, texRgn1);
    result.rgb = max(0, result.rgb + Offset);
    result.rgb *= (ExposureMinusOne + 1);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result = pLinearToPSRGB(result);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void GammaCompressedLightingResolvePixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    if (0) {
        float2 vpos = GET_VPOS / GetRenderTargetSize();
        result = float4(vpos.x, vpos.y, 0, 1);
        return;
    }

    result = ResolveCommon(texCoord1, texRgn1);

    result = GammaCompress(result);
    if (ResolveToSRGB)
        result = pLinearToPSRGB(result);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void ToneMappedLightingResolvePixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    if (0) {
        float2 vpos = GET_VPOS / GetRenderTargetSize();
        result = float4(vpos.x, vpos.y, 0, 1);
        return;
    }

    result = ResolveCommon(texCoord1, texRgn1);

    float3 preToneMap = max(0, result.rgb + Offset) * (ExposureMinusOne + 1);

    result = float4(Uncharted2Tonemap(preToneMap) / Uncharted2Tonemap1(WhitePoint), result.a);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result = pLinearToPSRGB(result);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void LightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    if (0) {
        float2 vpos = GET_VPOS / GetRenderTargetSize();
        result = float4(vpos.x, vpos.y, 0, 1);
        return;
    }

    result = ResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    result.rgb = max(0, result.rgb + Offset);
    result.rgb *= (ExposureMinusOne + 1);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result = pLinearToPSRGB(result);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void GammaCompressedLightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    if (0) {
        float2 vpos = GET_VPOS / GetRenderTargetSize();
        result = float4(vpos.x, vpos.y, 0, 1);
        return;
    }

    result = ResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    result = GammaCompress(result);
    if (ResolveToSRGB)
        result = pLinearToPSRGB(result);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void ToneMappedLightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    if (0) { 
        float2 vpos = GET_VPOS / GetRenderTargetSize();
        result = float4(vpos.x, vpos.y, 0, 1);
        return;
    }

    result = ResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    float3 preToneMap = max(0, result.rgb + Offset) * (ExposureMinusOne + 1);

    result = float4(Uncharted2Tonemap(preToneMap) / Uncharted2Tonemap1(WhitePoint), result.a);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result = pLinearToPSRGB(result);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

void CalculateLuminancePixelShader(
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    float4 coord = float4(clamp2(texCoord1, texRgn1.xy, texRgn1.zw), 0, 0);
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

technique CalculateLuminance
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 CalculateLuminancePixelShader();
    }
}