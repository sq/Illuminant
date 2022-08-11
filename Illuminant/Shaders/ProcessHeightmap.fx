#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"

uniform float3 TapSpacingAndBias;
uniform float2 DisplacementScale;

sampler HeightmapSampler {
    Texture = (BitmapTexture);
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP; 
};

float tap(
    float2 uv,
    float4 texRgn
) {
    float4 rgba = tex2Dbias(HeightmapSampler, float4(clamp(uv, texRgn.xy, texRgn.zw), 0, TapSpacingAndBias.z));
    return ExtractMask(rgba, BitmapTraits) - 0.5;
}

float3 calculateNormal(
    float2 texCoord, float4 texRgn
) {
    float3 spacing = float3(TapSpacingAndBias.xy, 0);
    return normalize(float3(
        tap(texCoord - spacing.xz, texRgn) - tap(texCoord + spacing.xz, texRgn),
        tap(texCoord - spacing.zy, texRgn) - tap(texCoord + spacing.zy, texRgn),
        tap(texCoord, texRgn)
    ));
}

void HeightmapToNormalsPixelShader (
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result       : COLOR0
) {
    float3 normal = calculateNormal(texCoord, texRgn);
    result = float4(normal + 0.5, 1);
}

void HeightmapToDisplacementPixelShader(
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float3 normal = calculateNormal(texCoord, texRgn);
    float3 displacement = normal.xyz * float3(DisplacementScale, 1);
    result = float4(displacement.xy + 0.5, 0.5, 1);
}

technique HeightmapToNormals
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HeightmapToNormalsPixelShader();
    }
}

technique HeightmapToDisplacement
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HeightmapToDisplacementPixelShader();
    }
}