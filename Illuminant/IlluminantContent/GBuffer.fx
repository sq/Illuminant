#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "GBufferShaderCommon.fxh"

void HeightVolumeVertexShader (
    in    float3   position      : POSITION0, // x, y, z
    inout float3   normal        : NORMAL0, 
    out   bool     dead          : TEXCOORD2,
    out   float3   worldPosition : TEXCOORD1,
    out   float4   result        : POSITION0
) {
    worldPosition = position;
    result = TransformPosition(float4((position.xy - Viewport.Position) * Viewport.Scale, 0, 1), 0);
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

    position.y -= getZToYMultiplier() * position.z;
    result = TransformPosition(float4((position.xy - Viewport.Position) * Viewport.Scale, 0, 1), 0);
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
        float relativeY = (getZToYMultiplier() * worldPosition.z);

        if (worldPosition.z < getGroundZ()) {
            discard;
            return;
        }

        // HACK: We drop the world x axis and the normal y axis,
        //  and reconstruct those two values when sampling the g-buffer
        result = float4(
            (normal.x / 2) + 0.5,
            (normal.z / 2) + 0.5,
            (relativeY / 512),
            (worldPosition.z / 512)
        );
    }
}

// Also used for the ground plane
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