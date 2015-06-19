#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

uniform float ZToYMultiplier;

void ScreenSpaceVertexShader(
    in float3 position : POSITION0, // x, y, z
    inout float4 color : COLOR0,
    out float4 result : POSITION0
) {
    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy, 0, 1), 0);
}

void VertexColorPixelShader(
    inout float4 color : COLOR0
) {
}

technique VolumeFrontFace
{
    pass P0
    {
        vertexShader = compile vs_1_1 ScreenSpaceVertexShader();
        pixelShader = compile ps_2_0  VertexColorPixelShader();
    }
}