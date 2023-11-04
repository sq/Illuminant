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

uniform const float3 DistancePowersAndMipBias = float3(1, 1, 0);

void HeightFromDistancePixelShader(
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    // min distance (0 for no interior), max distance (0 for no exterior), min height, max height
    in float4 params       : COLOR2,
    out float4 result      : COLOR0
) {
    float distance = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, DistancePowersAndMipBias.z)).r;
    distance = max(params.x, distance);

    if (distance > params.y) {
        result = 0;
        discard;
        return;
    }

    distance -= params.x;
    distance /= (params.y - params.x);
    distance = 1 - pow(1 - saturate(pow(distance, DistancePowersAndMipBias.x)), DistancePowersAndMipBias.y);
    // Negative distance is higher so we want to go from max height to min height as distance increases
    distance = lerp(params.w, params.z, distance);
    result = float4(distance, distance, distance, 1);
}

void HeightmapToNormalsPixelShader (
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result      : COLOR0
) {
    float alpha;
    float3 normal = calculateNormal(texCoord, texRgn, BitmapTexelSize, BitmapTraits, alpha);
    result = float4(NormalsAreSigned ? normal * DenormalCompensation.x : (normal * 0.5) + 0.5, alpha);
}

void HeightmapToDisplacementPixelShader (
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result      : COLOR0 
) {
    float alpha;
    float3 normal = calculateNormal(texCoord, texRgn, BitmapTexelSize, BitmapTraits, alpha);
    float3 displacement = normal.xyz * float3(DisplacementScale, 1);
    result = float4(displacement.xy + 0.5, 0.5, 1);
}

technique HeightFromDistance
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HeightFromDistancePixelShader();
    }
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