#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

#define SELF_OCCLUSION_HACK 1.5

uniform float  RenderScale;
uniform float  ZToYMultiplier;
// FIXME: Use the shared header?
uniform float3 DistanceFieldExtent;

Texture2D Mask : register(t0);
sampler MaskSampler : register(s0) {
    Texture = (Mask);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

void BillboardVertexShader(
    in    float3 position      : POSITION0, // x, y, z
    inout float2 texCoord      : TEXCOORD0,
    inout float3 normal        : NORMAL0,
    inout float3 worldPosition : TEXCOORD1,
    out   float4 result        : POSITION0
) {
    // HACK: Offset away from the surface to prevent self occlusion
    worldPosition += (SELF_OCCLUSION_HACK * normal);

    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = position.z / DistanceFieldExtent.z;
}

void BillboardPixelShader(
    in float2 texCoord      : TEXCOORD0,
    in float3 worldPosition : TEXCOORD1,
    in float2 vpos          : VPOS,
    in float3 normal        : NORMAL0,
    out float4 result       : COLOR0
) {
    float alpha = tex2D(MaskSampler, texCoord).a;

    const float discardThreshold = (1.0 / 255.0);
    clip(alpha - discardThreshold);

    // HACK: We drop the world x axis and the normal y axis,
    //  and reconstruct those two values when sampling the g-buffer
    result = float4(
        // HACK: For visualization
        (normal.x / 2) + 0.5,
        (normal.z / 2) + 0.5,
        worldPosition.y / 1024,
        worldPosition.z / 1024
    );
}

technique Billboard
{
    pass P0
    {
        vertexShader = compile vs_3_0 BillboardVertexShader();
        pixelShader = compile ps_3_0 BillboardPixelShader();
    }
}