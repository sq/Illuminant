#define ENABLE_DITHERING

#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\DitherCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "HDR.fxh"

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

#define FAST_RESOLVE
#define ResolveVertexShader ScreenSpaceVertexShader

uniform bool  ResolveToSRGB;
uniform float InverseScaleFactor;

float4 ResolveCommon (
    in float2 texCoord,
    in float4 texRgn
) {
    float4 result;

    float4 coord = float4(clamp(texCoord, texRgn.xy, texRgn.zw), 0, 0);
    float2 coordTexels = coord.xy * BitmapTextureSize;

    float4 sampleLinear = tex2Dlod(LinearSampler, coord);

#ifdef FAST_RESOLVE
    result = sampleLinear;
#else
    float4 samplePoint = tex2Dlod(PointSampler, coord);
    [branch]
    if (samplePoint.a > 0) {
        float2 topLeftTexels = floor(coordTexels);
        float2 bottomRightTexels = ceil(coordTexels);
        float2 xyWeight = coordTexels - topLeftTexels;

        float z[4];
        float normal[4];

        {
            float3 shadedPositionTL, shadedPositionTR, shadedPositionBL, shadedPositionBR;
            float3 shadedNormalTL, shadedNormalTR, shadedNormalBL, shadedNormalBR;

            sampleGBuffer(
                topLeftTexels / Environment.RenderScale,
                shadedPositionTL, shadedNormalTL
            );

            sampleGBuffer(
                float2(bottomRightTexels.x, topLeftTexels.y) / Environment.RenderScale,
                shadedPositionTR, shadedNormalTR
            );

            sampleGBuffer(
                float2(topLeftTexels.x, bottomRightTexels.y) / Environment.RenderScale,
                shadedPositionBL, shadedNormalBL
            );

            sampleGBuffer(
                bottomRightTexels / Environment.RenderScale,
                shadedPositionBR, shadedNormalBR
            );

            z[0] = shadedPositionTL.z;
            z[1] = shadedPositionTR.z;
            z[2] = shadedPositionBL.z;
            z[3] = shadedPositionBR.z;
        }

        float2 rcpSize = 1.0 / BitmapTextureSize;
        float4 lightTL, lightTR, lightBL, lightBR;

        const float windowStart = 1.5;
        const float windowSize = 1.5;
        const float windowEnd = windowStart + windowSize;

        // HACK
        float averageZ = (z[0] + z[1] + z[2] + z[3]) / 4;
        float blendWeight = (((
            clamp(abs(z[0] - averageZ), windowStart, windowEnd) +
            clamp(abs(z[1] - averageZ), windowStart, windowEnd) +
            clamp(abs(z[2] - averageZ), windowStart, windowEnd) +
            clamp(abs(z[3] - averageZ), windowStart, windowEnd)
        ) / 4) - windowStart) / windowSize;

        result = lerp(sampleLinear, samplePoint, blendWeight);
    }
    else {
        discard;
        return 0;
    }
#endif
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
    float4 light = ResolveCommon(texCoord1, texRgn1) * 2;

    texCoord2 = clamp(texCoord2, texRgn2.xy, texRgn2.zw);
    float4 albedo = tex2D(TextureSampler2, texCoord2);

    float4 result = albedo;
    result.rgb *= light.rgb;

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

    float3 rgbScaled = samplePoint.rgb * float3(0.299, 0.587, 0.144);
    float luminance = (rgbScaled.r + rgbScaled.g + rgbScaled.b);

    result = float4(luminance, luminance, luminance, luminance);
}

technique LightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ResolveVertexShader();
        pixelShader = compile ps_3_0 LightingResolvePixelShader();
    }
}

technique GammaCompressedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ResolveVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedLightingResolvePixelShader();
    }
}

technique ToneMappedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ResolveVertexShader();
        pixelShader = compile ps_3_0 ToneMappedLightingResolvePixelShader();
    }
}

technique LightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ResolveVertexShader();
        pixelShader = compile ps_3_0 LightingResolveWithAlbedoPixelShader();
    }
}

technique GammaCompressedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ResolveVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedLightingResolveWithAlbedoPixelShader();
    }
}

technique ToneMappedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ResolveVertexShader();
        pixelShader = compile ps_3_0 ToneMappedLightingResolveWithAlbedoPixelShader();
    }
}

technique CalculateLuminance
{
    pass P0
    {
        vertexShader = compile vs_3_0 ResolveVertexShader();
        pixelShader = compile ps_3_0 CalculateLuminancePixelShader();
    }
}