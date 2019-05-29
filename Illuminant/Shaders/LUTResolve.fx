#define ENABLE_DITHERING

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\DitherCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\LUTCommon.fxh"
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

float4 LUTBlendedResolveWithAlbedoCommon(
    in float2 texCoord1,
    in float4 texRgn1,
    in float2 texCoord2,
    in float4 texRgn2
) {
    texCoord1 = clamp2(texCoord1, texRgn1.xy, texRgn1.zw);
    texCoord2 = clamp2(texCoord2 + LightmapUVOffset, texRgn2.xy, texRgn2.zw);

    float4 light = tex2Dlod(TextureSampler2, float4(texCoord2, 0, 0));
    float4 albedo = tex2Dlod(TextureSampler, float4(texCoord1, 0, 0));

    light *= InverseScaleFactor * 2;

    float3 weight = light.rgb;
    float bandWidth = saturate(LUTLevels.z - LUTLevels.x);
    float neutralBandWidth = min(LUTLevels.y, bandWidth - 0.01);
    bool hasNeutralBand = (neutralBandWidth > 0);
    bool normalize = !PerChannelLUT || hasNeutralBand;
    if (normalize) {
        weight *= RgbToGray;
        weight = weight.r + weight.g + weight.b;
    }

    albedo.rgb = saturate3(albedo.rgb);

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

void LUTBlendedLightingResolveWithAlbedoPixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    result = LUTBlendedResolveWithAlbedoCommon(texCoord1, texRgn1, texCoord2, texRgn2);

    result.rgb = max(0, result.rgb + Offset);
    result.rgb *= (ExposureMinusOne + 1);
    result.rgb = pow(result.rgb, (GammaMinusOne + 1));
    if (ResolveToSRGB)
        result.rgb = LinearToSRGB(result.rgb);
    result.rgb = ApplyDither(result.rgb, GET_VPOS);
}

technique ScreenSpaceLUTBlendedLightingResolveWithAlbedo
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 LUTBlendedLightingResolveWithAlbedoPixelShader();
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
