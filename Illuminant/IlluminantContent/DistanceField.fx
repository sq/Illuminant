#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

uniform float2 PixelSize;
uniform float  SliceZ;
uniform int    NumVertices;

Texture2D VertexDataTexture : register(t5);
sampler   VertexDataSampler : register(s5) {
    Texture = (VertexDataTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU  = WRAP;
    AddressV  = WRAP;
};

void DistanceVertexShader (
    in    float3 position      : POSITION0, // x, y, z
    inout float2 zRange        : TEXCOORD0,
    out   float4 result        : POSITION0
) {
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = 0;
}

float computeDistance (
    float2 vpos, 
    float2 zRange,
    float  xySign
) {
    float resultDistance = 99999;
    float indexMultiplier = 1.0 / NumVertices;

    [loop]
    for (int i = 0; i < NumVertices; i++) {
        float2 edgeA = tex2Dlod(VertexDataSampler, float4(i * indexMultiplier, 0, 0, 0)).rg;
        float2 edgeB = tex2Dlod(VertexDataSampler, float4((i + 1) * indexMultiplier, 0, 0, 0)).rg;

        float2 closest = closestPointOnEdge(vpos, edgeA, edgeB);
        float2 closestDeltaXy = (vpos - closest);

        float closestDeltaZ = 0;
        float localSign = 1;
        float deltaMinZ = SliceZ - zRange.x;
        float deltaMaxZ = SliceZ - zRange.y;

        if ((SliceZ >= zRange.x) && (SliceZ <= zRange.y)) {
            closestDeltaZ = 0;
            localSign = xySign;
        } else if (abs(deltaMinZ) > abs(deltaMaxZ)) {
            closestDeltaZ = deltaMaxZ;
        } else {
            closestDeltaZ = deltaMinZ;
        }

        float3 closestDelta = float3(closestDeltaXy.x, closestDeltaXy.y, closestDeltaZ);
        float  closestDistance = length(closestDelta) * localSign;

        if (abs(closestDistance) < abs(resultDistance))
            resultDistance = closestDistance;
    }

    return resultDistance;
}

void InteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    in  float2 vpos : VPOS
) {
    vpos *= DistanceFieldInvScaleFactor;
    vpos += ViewportPosition;
    float resultDistance = computeDistance(vpos, zRange, -1);
    color = encodeDistance(resultDistance);
}

void ExteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    in  float2 vpos  : VPOS
) {
    vpos *= DistanceFieldInvScaleFactor;
    vpos += ViewportPosition;
    float resultDistance = computeDistance(vpos, zRange, 1);
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