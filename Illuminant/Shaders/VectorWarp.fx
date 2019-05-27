#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"

sampler FieldSampler : register(s6) {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

uniform float3 FieldIntensity;

void VectorWarpPixelShader (
    in float4 multiplyColor : COLOR0,
    in float2 texCoord1     : TEXCOORD0,
    in float4 texRgn1       : TEXCOORD1,
    in float2 texCoord2     : TEXCOORD2,
    in float4 texRgn2       : TEXCOORD3,
    ACCEPTS_VPOS,
    out float4 result       : COLOR0
) {
    float2 fieldTexCoord = clamp(texCoord1, texRgn1.xy, texRgn1.zw);
    float4 rawFieldValue = tex2D(FieldSampler, fieldTexCoord);
    float3 adjustedFieldValue = (rawFieldValue.xyz - 0.5) * 2;
    float l = length(adjustedFieldValue);

    float3 fieldValue;
    if (l >= 0.01)
        fieldValue = (adjustedFieldValue / l) * FieldIntensity;
    else
        fieldValue = 0;

    float2 baseTexCoord = (GET_VPOS * HalfTexel2);
    float2 warpedTexCoord = baseTexCoord + (fieldValue.xy * HalfTexel2);
    float2 finalTexCoord = clamp(warpedTexCoord, texRgn2.xy, texRgn2.zw);
    float4 background = tex2D(TextureSampler2, finalTexCoord);

    result = background;
    result *= multiplyColor;
    result *= rawFieldValue.a;

    const float discardThreshold = (0.5 / 255.0);
    clip(rawFieldValue.a - discardThreshold);
}

technique ScreenSpaceVectorWarp
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 VectorWarpPixelShader();
    }
}