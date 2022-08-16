#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"

sampler FieldSampler : register(s6) {
    Texture = (SecondTexture);
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

sampler HeightmapSampler {
    Texture = (SecondTexture);
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

uniform float3 FieldIntensity;
uniform float2 RefractionIndexAndMipBias;

#include "ProcessHeightmap.fxh"

void VectorWarpPixelShader (
    in float4 multiplyColor : COLOR0,
    in float2 texCoord1     : TEXCOORD0,
    in float4 texRgn1       : TEXCOORD1,
    in float2 texCoord2     : TEXCOORD2,
    in float4 texRgn2       : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result       : COLOR0
) {
    float2 fieldTexCoord = clamp2(texCoord2, texRgn2.xy, texRgn2.zw);
    float4 rawFieldValue = tex2D(FieldSampler, fieldTexCoord);
    float3 adjustedFieldValue = (rawFieldValue.xyz - 0.5) * 2;
    float l = length(adjustedFieldValue);

    float3 fieldValue;
    if (l >= 0.01)
        fieldValue = (adjustedFieldValue / l) * FieldIntensity;
    else
        fieldValue = 0;

    float2 baseTexCoord = (GET_VPOS * HalfTexel);
    float2 warpedTexCoord = baseTexCoord + (fieldValue.xy * HalfTexel);
    float2 finalTexCoord = clamp2(warpedTexCoord, texRgn1.xy, texRgn1.zw);
    float4 background = tex2D(TextureSampler, finalTexCoord);
    background = ExtractRgba(background, BitmapTraits);

    result = background;
    result *= multiplyColor;
    result *= rawFieldValue.a;

    const float discardThreshold = (0.5 / 255.0);
    clip(rawFieldValue.a - discardThreshold);
}

void NormalRefractionPixelShader(
    in float4 multiplyColor : COLOR0,
    in float2 texCoord1     : TEXCOORD0,
    in float4 texRgn1       : TEXCOORD1,
    in float2 texCoord2     : TEXCOORD2,
    in float4 texRgn2       : TEXCOORD3,
    out float4 result       : COLOR0
) {
    float2 normalTexCoord = clamp2(texCoord2, texRgn2.xy, texRgn2.zw);
    float4 rawNormals = tex2D(TextureSampler2, normalTexCoord);
    float3 surfaceNormal = NormalsAreSigned 
        ? rawNormals.xyz * DenormalCompensation.y
        : (rawNormals.xyz - 0.5) * 2;

    float3 ray = float3(0, 0, -1);
    float3 refracted = refract(ray, normalize(surfaceNormal), RefractionIndexAndMipBias.x);
    float3 bias = refracted * FieldIntensity;
    // FIXME: z would be nice to produce a lensing effect (via some sort of ray-plane intersection)
    //  but I wasn't able to get it to work when I tried
    float3 intersectionPoint = float3(texCoord1.xy + bias.xy, 0);
    // The generated normal map has alpha values of 0 in areas where the heightmap was 0 or we otherwise don't
    //  want to apply refraction mip bias
    float effectiveMipBias = RefractionIndexAndMipBias.y * rawNormals.a;
    float4 warped = tex2Dbias(TextureSampler, float4(clamp2(intersectionPoint.xy, texRgn1.xy, texRgn1.zw), 0, effectiveMipBias)),
        unwarped = tex2D(TextureSampler, clamp2(texCoord1, texRgn1.xy, texRgn1.zw));
    warped = ExtractRgba(warped, BitmapTraits);
    unwarped = ExtractRgba(unwarped, BitmapTraits);

    // We want to smoothly interpolate between unwarped and warped in boundary regions where the height just changed,
    //  because in those regions the refracted vector is likely to be almost perfectly horizontal so the resulting pixel
    //  values will look completely wrong and cause an ugly hard edge.
    // We negate and multiply the refracted z value to do this simply, our warping magnitude will increase as the z length does
    //  and if the z value is negative we won't warp at all (since it's pointing away from the bitmap)
    result = lerp(unwarped, warped, saturate(refracted.z * -33));
    result.a = 1.0;
    result *= multiplyColor;

    const float discardThreshold = (0.5 / 255.0);
    clip(result.a - discardThreshold);
}

void HeightmapRefractionPixelShader(
    in float4 multiplyColor : COLOR0,
    in float2 texCoord1 : TEXCOORD0,
    in float4 texRgn1 : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    float alpha;
    float3 surfaceNormal = calculateNormal(texCoord2, texRgn2, HalfTexel2, BitmapTraits2, alpha);

    float3 ray = float3(0, 0, -1);
    float3 refracted = refract(ray, normalize(surfaceNormal), RefractionIndexAndMipBias.x);
    float3 bias = refracted * FieldIntensity;
    // FIXME: z would be nice to produce a lensing effect (via some sort of ray-plane intersection)
    //  but I wasn't able to get it to work when I tried
    float3 intersectionPoint = float3(texCoord1.xy + bias.xy, 0);
    // The generated normal map has alpha values of 0 in areas where the heightmap was 0 or we otherwise don't
    //  want to apply refraction mip bias
    float effectiveMipBias = RefractionIndexAndMipBias.y * alpha;
    float4 warped = tex2Dbias(TextureSampler, float4(clamp2(intersectionPoint.xy, texRgn1.xy, texRgn1.zw), 0, effectiveMipBias)),
        unwarped = tex2D(TextureSampler, clamp2(texCoord1, texRgn1.xy, texRgn1.zw));
    warped = ExtractRgba(warped, BitmapTraits);
    unwarped = ExtractRgba(unwarped, BitmapTraits);

    // We want to smoothly interpolate between unwarped and warped in boundary regions where the height just changed,
    //  because in those regions the refracted vector is likely to be almost perfectly horizontal so the resulting pixel
    //  values will look completely wrong and cause an ugly hard edge.
    // We negate and multiply the refracted z value to do this simply, our warping magnitude will increase as the z length does
    //  and if the z value is negative we won't warp at all (since it's pointing away from the bitmap)
    result = lerp(unwarped, warped, saturate(refracted.z * -33));
    result.a = 1.0;
    result *= multiplyColor;

    const float discardThreshold = (0.5 / 255.0);
    clip(result.a - discardThreshold);
}

technique ScreenSpaceVectorWarp
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 VectorWarpPixelShader();
    }
}

technique ScreenSpaceNormalRefraction
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 NormalRefractionPixelShader();
    }
}

technique ScreenSpaceHeightmapRefraction
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HeightmapRefractionPixelShader();
    }
}