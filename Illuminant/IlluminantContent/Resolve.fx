#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

sampler PointSampler : register(s7) {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void LightingResolvePixelShader(
    in float4 multiplyColor : COLOR0, 
    in float4 addColor : COLOR1, 
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float2 coord       = clamp(texCoord, texTL, texBR);
    float2 coordTexels = coord * BitmapTextureSize;

    float2 topLeftTexels     = floor(coordTexels);
    float2 bottomRightTexels = ceil(coordTexels);
    float2 xyWeight = coordTexels - topLeftTexels;

    float zTL, zTR, zBL, zBR;
    float normTL, normTR, normBL, normBR;

    {
        float3 shadedPositionTL, shadedPositionTR, shadedPositionBL, shadedPositionBR;
        float3 shadedNormalTL,   shadedNormalTR,   shadedNormalBL,   shadedNormalBR;

        sampleGBuffer(
            topLeftTexels * RenderScale,
            shadedPositionTL, shadedNormalTL
        );

        sampleGBuffer(
            float2(bottomRightTexels.x, topLeftTexels.y) * RenderScale,
            shadedPositionTR, shadedNormalTR
        );

        sampleGBuffer(
            float2(topLeftTexels.x, bottomRightTexels.y) * RenderScale,
            shadedPositionBL, shadedNormalBL
        );

        sampleGBuffer(
            bottomRightTexels * RenderScale,
            shadedPositionBR, shadedNormalBR
        );

        zTL = shadedPositionTL.z;
        zTR = shadedPositionTR.z;
        zBL = shadedPositionBL.z;
        zBR = shadedPositionBR.z;

        normTL = shadedNormalTL.y;
        normTR = shadedNormalTR.y;
        normBL = shadedNormalBL.y;
        normBR = shadedNormalBR.y;
    }

    float2 rcpSize = 1.0 / BitmapTextureSize;
    float4 lightTL, lightTR, lightBL, lightBR;

    lightTL = tex2D(PointSampler, topLeftTexels * rcpSize);
    lightTR = tex2D(PointSampler, float2(bottomRightTexels.x, topLeftTexels.y) * rcpSize);
    lightBL = tex2D(PointSampler, float2(topLeftTexels.x, bottomRightTexels.y) * rcpSize);
    lightBR = tex2D(PointSampler, bottomRightTexels * rcpSize);

    float weightTL = (1 - xyWeight.x) * (1 - xyWeight.y),
        weightTR = xyWeight.x * (1 - xyWeight.y), 
        weightBL = (1 - xyWeight.x) * xyWeight.y, 
        weightBR = xyWeight.x * xyWeight.y;

    float4 interpolatedLight = (
        (lightTL * weightTL) +
        (lightTR * weightTR) +
        (lightBL * weightBL) +
        (lightBR * weightBR)
    );

    result = multiplyColor * interpolatedLight; 
    result += (addColor * result.a);
}

technique LightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolvePixelShader();
    }
}
