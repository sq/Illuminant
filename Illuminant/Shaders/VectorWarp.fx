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

uniform float3 FieldIntensity;
uniform float RefractionIndex;

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
    float3 surfaceNormal = (rawNormals.xyz - 0.5) * 2;

    float3 ray = float3(0, 0, -1);
    float3 refracted = refract(ray, surfaceNormal, RefractionIndex);
    float3 bias = refracted * FieldIntensity;
    float2 warpedTexCoord = texCoord1 + bias.xy; // FIXME: z
    float4 warped = tex2D(TextureSampler, clamp2(warpedTexCoord, texRgn1.xy, texRgn1.zw)),
        unwarped = tex2D(TextureSampler, clamp2(texCoord1, texRgn1.xy, texRgn1.zw));
    warped = ExtractRgba(warped, BitmapTraits);
    unwarped = ExtractRgba(unwarped, BitmapTraits);

    // HACK: Fade out if the refracted ray does not point downward to a meaningful extent
    result = lerp(unwarped, warped, saturate(-refracted.z * 10));
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