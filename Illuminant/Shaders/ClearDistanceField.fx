#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"

Texture2D ClearTexture : register(t0);
sampler   ClearSampler : register(s0) {
    Texture = (ClearTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};
uniform const float2 ClearTextureSize <string sizeInPixelsOf="ClearTexture"; bool hidden=true;>;

void DistanceVertexShader (
    in    float3 position : POSITION0, // x, y, z
    inout float4 color    : COLOR0,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - GetViewportPosition(), 0, 1), 0);
    result.z = 0;
}

void ClearPixelShader (
    inout float4 color : COLOR0,
    ACCEPTS_VPOS
) {
    [branch]
    if (ClearTextureSize.x > 1) {
        float2 vp = (GET_VPOS + 0.5) / ClearTextureSize;
        float4 tex = tex2Dlod(ClearSampler, float4(vp.x, vp.y, 0, 0));
        color = tex;
    }
}

technique ClearDistanceField
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceVertexShader();
        pixelShader  = compile ps_3_0 ClearPixelShader();
    }
}