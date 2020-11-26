#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "GBufferShaderCommon.fxh"

Texture2D GBuffer      : register(t0);
sampler   GBufferSampler : register(s0) {
    Texture = (GBuffer);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void MaskVertexShader(
    // x, y, groundZ, maximumZ
    in    float4 position       : POSITION0,
    inout float2 texCoord       : TEXCOORD0,
    out   float2 zRange         : NORMAL0,
    out   float4 result         : POSITION0
) {
    zRange = position.zw;
    result = float4(position.xy * float2(1, -1), 0, 1);
}

void MaskPixelShader(
    in  float2 texCoord       : TEXCOORD0,
    in  float2 zRange         : NORMAL0,
    out float4 result         : COLOR0
) {
    float4 g = tex2Dlod(GBufferSampler, float4(texCoord, 0, 0));

    // Unshadowed pixels have their Z set to (-z - 1)
    //  so the minimum value is (-maximumZ - 1) and the max is (-groundZ - 1)
    float minW = -abs(zRange.y) - 1;
    float maxW = -abs(zRange.x) - 1;

    if ((g.w >= 9999) || (g.w < minW) || ((g.w < 0) && (g.w > maxW))) {
        result = 0;
        discard;
    } else {
        result = g;
    }
}

technique GBufferMask
{
    pass P0
    {
        vertexShader = compile vs_3_0 MaskVertexShader();
        pixelShader = compile ps_3_0 MaskPixelShader();
    }
}