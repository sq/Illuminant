#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

#define SELF_OCCLUSION_HACK 1

uniform float  ZToYMultiplier;
uniform float3 DistanceFieldExtent;

void HeightVolumeVertexShader (
    in  float3 position      : POSITION0, // x, y, z
    out float3 worldPosition : TEXCOORD1,
    out float4 result        : POSITION0
) {
    worldPosition = position;
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = 0;
}

void HeightVolumeFaceVertexShader(
    in  float3 position      : POSITION0, // x, y, z
    in  float3 normal        : NORMAL0,
    out float3 worldPosition : TEXCOORD1,
    out float4 result        : POSITION0
) {
    // HACK: Offset away from the surface to prevent self occlusion
    worldPosition = position + (SELF_OCCLUSION_HACK * normal);

    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = position.z / DistanceFieldExtent.z;
}

void HeightVolumePixelShader (
    in float2 vpos : VPOS,
    in float3 worldPosition : TEXCOORD1,
    out float4 color : COLOR0
) {
    color = float4(
        worldPosition,
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

technique HeightVolumeFace
{
    pass P0
    {
        vertexShader = compile vs_3_0 HeightVolumeFaceVertexShader();
        pixelShader = compile ps_3_0 HeightVolumePixelShader();
    }
}