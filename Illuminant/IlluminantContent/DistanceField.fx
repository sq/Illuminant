#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

uniform float2 PixelSize;
uniform float  SliceZ;
uniform int    NumVertices;

Texture2D VertexDataTexture : register(t0);
sampler   VertexDataSampler : register(s0) {
    Texture = (VertexDataTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU  = CLAMP;
    AddressV  = CLAMP;
};

void DistanceVertexShader (
    in    float3 position : POSITION0, // x, y, z
    inout float2 zRange   : TEXCOORD0,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - Viewport.Position, 0, 1), 0);
    result.z = 0;
}

float computeDistanceToEdge (
    float2 vpos, float closestDeltaZ, float u
) {
    float4 packedEdge = tex2Dlod(VertexDataSampler, float4(u, 0, 0, 0));
    float2 edgeA = packedEdge.xy;
    float2 edgeB = packedEdge.zw;

    float2 closest = closestPointOnEdge(vpos, edgeA, edgeB);
    float2 closestDeltaXy = (vpos - closest);

    float3 closestDelta = float3(closestDeltaXy.x, closestDeltaXy.y, closestDeltaZ);
    float  closestDistance = length(closestDelta);

    return closestDistance;
}

float computeDistance (
    float2 vpos, float2 zRange
) {
    float resultDistance = 99999;
    float indexMultiplier = 1.0 / NumVertices;
    float u = 0;

    float deltaMinZ = SliceZ - zRange.x;
    float deltaMaxZ = SliceZ - zRange.y;

    float closestDeltaZ;
    if ((SliceZ >= zRange.x) && (SliceZ <= zRange.y)) {
        closestDeltaZ = 0;
    }
    else if (abs(deltaMinZ) > abs(deltaMaxZ)) {
        closestDeltaZ = deltaMaxZ;
    }
    else {
        closestDeltaZ = deltaMinZ;
    }

    [loop]
    for (int i = 0; i < NumVertices; i += 4) {
        // fxc can't handle unrolling loops without spending 30 minutes, yaaaaaaaaaay
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, closestDeltaZ, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, closestDeltaZ, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, closestDeltaZ, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, closestDeltaZ, u));
        u += indexMultiplier;
    }

    return resultDistance;
}

void InteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    in  float2 vpos : VPOS
) {
    vpos *= DistanceField.InvScaleFactor;
    vpos += Viewport.Position;

    float resultDistance;
    if ((SliceZ >= zRange.x) && (SliceZ <= zRange.y)) {
        resultDistance = -computeDistance(vpos, zRange);
    } else {
        resultDistance = min(abs(SliceZ - zRange.x), abs(SliceZ - zRange.y));
    }

    color = encodeDistance(resultDistance);
}

void ExteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    in  float2 vpos  : VPOS
) {
    vpos *= DistanceField.InvScaleFactor;
    vpos += Viewport.Position;
    float resultDistance = computeDistance(vpos, zRange);
    color = encodeDistance(resultDistance);
}

technique Exterior
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceVertexShader();
        pixelShader  = compile ps_3_0 ExteriorPixelShader();
    }
}

technique Interior
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceVertexShader();
        pixelShader = compile ps_3_0 InteriorPixelShader();
    }
}