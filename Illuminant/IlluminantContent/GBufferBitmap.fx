#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
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
    in    float3 position       : POSITION0, // x, y, z
    inout float2 texCoord       : TEXCOORD0,
    inout float3 normal         : NORMAL0,
    inout float  dataScale      : NORMAL1,
    inout float3 worldPosition  : TEXCOORD1,
    out   float3 screenPosition : TEXCOORD2,
    out   float4 result         : POSITION0
) {
    // HACK: Offset away from the surface to prevent self occlusion
    worldPosition += (SELF_OCCLUSION_HACK * normal);
    screenPosition = float3(position.xy - Viewport.Position, position.z);

    result = TransformPosition(float4(position.xy - Viewport.Position, 0, 1), 0);
    result.z = position.z / DistanceFieldExtent.z;
}

void MaskBillboardPixelShader(
    in float2 texCoord       : TEXCOORD0,
    in float3 worldPosition  : TEXCOORD1,
    in float3 screenPosition : TEXCOORD2,
    in float3 normal         : NORMAL0,
    out float4 result        : COLOR0
) {
    float alpha = tex2D(MaskSampler, texCoord).a;

    const float discardThreshold = (1.0 / 255.0);
    clip(alpha - discardThreshold);

    float wp = worldPosition.y;
    float sp = screenPosition.y;
    float relativeY = wp - sp;

    // HACK: We drop the world x axis and the normal y axis,
    //  and reconstruct those two values when sampling the g-buffer
    result = float4(
        (normal.x / 2) + 0.5,
        (normal.z / 2) + 0.5,
        (relativeY / 512),
        (worldPosition.z / 512)
    );
}

void GDataBillboardPixelShader(
    in float2 texCoord       : TEXCOORD0,
    in float3 worldPosition  : TEXCOORD1,
    in float3 screenPosition : TEXCOORD2,
    in float3 normal         : NORMAL0,
    in float  dataScale      : NORMAL1,
    out float4 result        : COLOR0
) {
    float4 data = tex2D(MaskSampler, texCoord);
    float alpha = data.a;

    const float discardThreshold = (127.0 / 255.0);
    clip(alpha - discardThreshold);

    float yOffset = data.b * dataScale;
    float effectiveZ = worldPosition.z + (yOffset / ZToYMultiplier);

    // HACK: We drop the world x axis and the normal y axis,
    //  and reconstruct those two values when sampling the g-buffer
    result = float4(
        data.r,
        data.g,
        yOffset / 512,
        effectiveZ / 512
    );
}

technique MaskBillboard
{
    pass P0
    {
        vertexShader = compile vs_3_0 BillboardVertexShader();
        pixelShader = compile ps_3_0 MaskBillboardPixelShader();
    }
}

technique GDataBillboard
{
    pass P0
    {
        vertexShader = compile vs_3_0 BillboardVertexShader();
        pixelShader = compile ps_3_0 GDataBillboardPixelShader();
    }
}