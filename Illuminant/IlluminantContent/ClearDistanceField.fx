#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

Texture2D ClearTexture : register(t0);
sampler   ClearSampler : register(s0) {
    Texture = (ClearTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

uniform float2 ClearInverseScale;
uniform float4 ClearMultiplier;

void DistanceVertexShader (
    in    float3 position : POSITION0, // x, y, z
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - Viewport.Position, 0, 1), 0);
    result.z = 0;
}

void ClearPixelShader (
    out float4 color : COLOR0,
    in  float2 vpos : VPOS
) {
    vpos *= ClearInverseScale;

    float4 tex = tex2Dlod(ClearSampler, float4(vpos.x, vpos.y, 0, 0));

    color = tex * ClearMultiplier;
}

technique Clear
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceVertexShader();
        pixelShader  = compile ps_3_0 ClearPixelShader();
    }
}