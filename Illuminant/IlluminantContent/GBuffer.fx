#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5

uniform float  RenderScale;
uniform float  ZToYMultiplier;
// FIXME: Use the shared header?
uniform float3 DistanceFieldExtent;

void HeightVolumeVertexShader (
    in    float3   position      : POSITION0, // x, y, z
    inout float3   normal        : NORMAL0, 
    out   bool     dead          : TEXCOORD2,
    out   float3   worldPosition : TEXCOORD1,
    out   float4   result        : POSITION0
) {
    worldPosition = position;
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = 0;
    dead = worldPosition.z < -9999;
}

void HeightVolumeFaceVertexShader(
    in    float3 position      : POSITION0, // x, y, z
    inout float3 normal        : NORMAL0,
    out   float3 worldPosition : TEXCOORD1,
    out   bool   dead          : TEXCOORD2,
    out   float4 result        : POSITION0
) {
    // HACK: Offset away from the surface to prevent self occlusion
    worldPosition = position + (SELF_OCCLUSION_HACK * normal);

    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = position.z / DistanceFieldExtent.z;
    dead = false;
}

void HeightVolumePixelShader (
    in float2  vpos          : VPOS,
    in float3  normal        : NORMAL0,
    in float3  worldPosition : TEXCOORD1,
    in bool    dead          : TEXCOORD2,
    out float4 result        : COLOR0
) {
    if (dead) {
        result = float4(
            0, 0,
            -99999,
            -99999
        );
    } else {
        float wp = worldPosition.y;
        float sp = vpos.y;
        float relativeY = wp - sp;

        // HACK: We drop the world x axis and the normal y axis,
        //  and reconstruct those two values when sampling the g-buffer
        result = float4(
            // HACK: For visualization
            (normal.x / 2) + 0.5,
            (normal.z / 2) + 0.5,
            (relativeY / 512),
            (worldPosition.z / 512)
        );
    }
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