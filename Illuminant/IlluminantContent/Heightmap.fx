#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

uniform float MaxZ;

void HeightVolumeVertexShader (
    in    float3 position : POSITION0, // x, y, z
    inout float2 zRange   : TEXCOORD0,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = 0;
}

void HeightVolumePixelShader (
    in float2 zRange : TEXCOORD0,
    out float4 color : COLOR0
) {
    color = zRange.y / MaxZ;
}

technique HeightVolume
{
    pass P0
    {
        vertexShader = compile vs_3_0 HeightVolumeVertexShader();
        pixelShader  = compile ps_3_0 HeightVolumePixelShader();
    }
}