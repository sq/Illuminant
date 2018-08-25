#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
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

uniform bool  ResolveToSRGB;
uniform float InverseScaleFactor;

float4 ResolveCommon (
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2
) {
    float4 result;

    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 coord = float4(clamp(texCoord, texTL, texBR), 0, 0);
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
    result *= InverseScaleFactor * multiplyColor;
    result += (addColor * result.a);
    result.a = 1;
    return result;
}

void LightingResolvePixelShader (
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveCommon(
        multiplyColor,
        addColor,
        texCoord,
        texTL,
        texBR
    );

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
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveCommon(
        multiplyColor,
        addColor,
        texCoord,
        texTL,
        texBR
    );

    result = GammaCompress(result);
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void ToneMappedLightingResolvePixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    result = ResolveCommon(
        multiplyColor,
        addColor,
        texCoord,
        texTL,
        texBR
    );

    float3 preToneMap = max(0, result.rgb + Offset) * (ExposureMinusOne + 1);

    result = float4(Uncharted2Tonemap(preToneMap) / Uncharted2Tonemap1(WhitePoint), result.a);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, vpos);
}

void CalculateLuminancePixelShader(
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float4 coord = float4(clamp(texCoord, texTL, texBR), 0, 0);
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
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolvePixelShader();
    }
}

technique GammaCompressedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 GammaCompressedLightingResolvePixelShader();
    }
}

technique ToneMappedLightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 ToneMappedLightingResolvePixelShader();
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