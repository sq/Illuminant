#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

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

    float z[4];
    float normal[4];

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

        z[0] = shadedPositionTL.z;
        z[1] = shadedPositionTR.z;
        z[2] = shadedPositionBL.z;
        z[3] = shadedPositionBR.z;
    }

    float2 rcpSize = 1.0 / BitmapTextureSize;
    float4 lightTL, lightTR, lightBL, lightBR;

    float4 samplePoint = tex2D(PointSampler, coord);
    float4 sampleLinear = tex2D(LinearSampler, coord);

    const float windowStart = 1.5;
    const float windowSize  = 1.5;
    const float windowEnd   = windowStart + windowSize;

    // HACK
    float averageZ = (z[0] + z[1] + z[2] + z[3]) / 4;
    float blendWeight = (((
        clamp(abs(z[0] - averageZ), windowStart, windowEnd) +
        clamp(abs(z[1] - averageZ), windowStart, windowEnd) +
        clamp(abs(z[2] - averageZ), windowStart, windowEnd) +
        clamp(abs(z[3] - averageZ), windowStart, windowEnd)
    ) / 4) - windowStart) / windowSize;

    result = lerp(sampleLinear, samplePoint, blendWeight) * multiplyColor;

    result += (addColor * result.a);

    result.a = 1;
}

technique LightingResolve
{
    pass P0
    {
        vertexShader = compile vs_3_0 WorldSpaceVertexShader();
        pixelShader = compile ps_3_0 LightingResolvePixelShader();
    }
}
