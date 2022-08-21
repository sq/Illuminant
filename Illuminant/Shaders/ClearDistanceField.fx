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

uniform const float2 ClearInverseScale;
uniform const float4 ClearMultiplier;

void DistanceVertexShader (
    in    float3 position : POSITION0, // x, y, z
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - GetViewportPosition(), 0, 1), 0);
    result.z = 0;
}

void ClearPixelShader (
    out float4 color : COLOR0,
    ACCEPTS_VPOS
) {
    PREFER_BRANCH
    if (ClearMultiplier.a > 0) {
        float2 vp = (GET_VPOS + 0.5) * ClearInverseScale;
        float4 tex = tex2Dlod(ClearSampler, float4(vp.x, vp.y, 0, 0));
        color = tex * ClearMultiplier;
    } else {
        color = float4(0, 0, 0, 0);
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