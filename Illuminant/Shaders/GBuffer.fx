#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "GBufferShaderCommon.fxh"

float ZSelfOcclusionHack;

void GroundPlaneVertexShader (
    in    float3   position      : POSITION0, // x, y, z
    inout float3   normal        : NORMAL0, 
    out   bool     dead          : TEXCOORD2,
    out   float3   worldPosition : TEXCOORD1,
    inout float    enableShadows : TEXCOORD4,
    out   float4   result        : POSITION0
) {
    worldPosition = position;
    result = TransformPosition(float4((position.xy - GetViewportPosition()) * GetViewportScale(), 0, 1), 0);
    result.z = 0;
    dead = worldPosition.z < -9999;
}

void HeightVolumeVertexShader (
    in    float3 position      : POSITION0, // x, y, z
    inout float3 normal        : NORMAL0,
    out   float3 worldPosition : TEXCOORD1,
    out   bool   dead          : TEXCOORD2,
    out   float4 midTransform  : TEXCOORD3,
    inout float  enableShadows : TEXCOORD4,
    out   float4 result        : POSITION0
) {
    worldPosition = position;

    position.y -= getZToYMultiplier() * position.z;
    midTransform = float4((position.xy - GetViewportPosition()) * GetViewportScale(), 0, 1);
    result = TransformPosition(midTransform, 0);
    result.z = position.z / DistanceFieldExtent.z;
    result.w = 1;
    dead = false;
}

void HeightVolumeFaceVertexShader(
    in    float3 position      : POSITION0, // x, y, z
    inout float3 normal        : NORMAL0,
    out   float3 worldPosition : TEXCOORD1,
    out   bool   dead          : TEXCOORD2,
    out   float4 midTransform  : TEXCOORD3,
    inout float  enableShadows : TEXCOORD4,
    out   float4 result        : POSITION0
) {
    worldPosition = position;

    position.y -= getZToYMultiplier() * position.z;
    midTransform = float4((position.xy - GetViewportPosition()) * GetViewportScale(), 0, 1);
    result = TransformPosition(midTransform, 0);
    result.z = position.z / DistanceFieldExtent.z;
    result.w = 1;
    dead = false;
}

void GroundPlanePixelShader (
    in float3  worldPosition : TEXCOORD1,
    in bool    dead          : TEXCOORD2,
    in float   enableShadows : TEXCOORD4,
    out float4 result        : COLOR0
) {
    if (worldPosition.z < getGroundZ()) {
        discard;
        return;
    }

    float3 normal = float3(0, 0, 1);
    result = encodeGBufferSample(normal, 0, worldPosition.z, dead, enableShadows > 0.5, false); 
}

void HeightVolumePixelShader(
    in float3  normal        : NORMAL0,
    in float3  worldPosition : TEXCOORD1,
    in bool    dead          : TEXCOORD2,
    in float   enableShadows : TEXCOORD4,
    out float4 result        : COLOR0
) {
    // HACK: Offset away from the surface to prevent self occlusion
    float3 selfOcclusionBias = float3(0, 0, ZSelfOcclusionHack);

    float relativeY = ((worldPosition.z * getZToYMultiplier()) * GetViewportScale() / getEnvironmentRenderScale()) + selfOcclusionBias.y;
    result = encodeGBufferSample(normal, relativeY, worldPosition.z + selfOcclusionBias.z, dead, enableShadows > 0.5, false);
}

void HeightVolumeFacePixelShader(
    in float3  normal        : NORMAL0,
    in float3  worldPosition : TEXCOORD1,
    in bool    dead          : TEXCOORD2,
    in float   enableShadows : TEXCOORD4,
    out float4 result        : COLOR0
) {
    if (worldPosition.z < getGroundZ()) {
        discard;
        return;
    }

    // HACK: Offset away from the surface to prevent self occlusion
    float3 selfOcclusionBias = float3(SelfOcclusionHack, SelfOcclusionHack, ZSelfOcclusionHack) * normal;

    float relativeY = ((worldPosition.z * getZToYMultiplier()) * GetViewportScale() / getEnvironmentRenderScale()) + selfOcclusionBias.y;
    result = encodeGBufferSample(normal, relativeY, worldPosition.z + selfOcclusionBias.z, dead, enableShadows > 0.5, false);
}

technique GroundPlane
{
    pass P0
    {
        vertexShader = compile vs_3_0 GroundPlaneVertexShader();
        pixelShader  = compile ps_3_0 GroundPlanePixelShader();
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
        pixelShader  = compile ps_3_0 HeightVolumeFacePixelShader();
    }
}