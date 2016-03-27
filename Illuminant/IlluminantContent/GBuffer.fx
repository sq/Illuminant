#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

void HeightVolumeVertexShader (
    in    float3 position : POSITION0, // x, y, z
    inout float2 zRange   : TEXCOORD0,
    out   float3 worldPosition : TEXCOORD1,
    out   float4 result   : POSITION0
) {
    worldPosition = float3(position.x, position.y, zRange.y);
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = 0;
}

void HeightVolumePixelShader (
    in float2 vpos : VPOS,
    in float2 zRange : TEXCOORD0,
    in float3 worldPosition : TEXCOORD1,
    out float4 color : COLOR0
) {
    // HACK: For viz purposes
    float scale = 1.0 / 1024.0;

    color = float4(
        vpos.x * scale,
        vpos.y * scale,
        zRange.y * scale,
        1
    );
}

technique HeightVolume
{
    pass P0
    {
        vertexShader = compile vs_3_0 HeightVolumeVertexShader();
        pixelShader  = compile ps_3_0 HeightVolumePixelShader();
    }
}