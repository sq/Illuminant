#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"

sampler HeightmapSampler {
    Texture = (BitmapTexture);
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

#include "ProcessHeightmap.fxh"

void HeightmapToNormalsPixelShader (
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result      : COLOR0
) {
    float alpha;
    float3 normal = calculateNormal(texCoord, texRgn, HalfTexel, BitmapTraits, alpha);
    result = float4(NormalsAreSigned ? normal * DenormalCompensation.x : (normal * 0.5) + 0.5, alpha);
}

void HeightmapToDisplacementPixelShader (
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result      : COLOR0 
) {
    float alpha;
    float3 normal = calculateNormal(texCoord, texRgn, HalfTexel, BitmapTraits, alpha);
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