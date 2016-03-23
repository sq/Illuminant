#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

uniform float2 PixelSize;
uniform float  MinZ, MaxZ;
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
    out   float4 result        : POSITION0
) {
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = position.z;
}

float computeDistance (
    float2 vpos
) {
    float resultDistance = 99999;
    float indexMultiplier = 1.0 / NumVertices;

    for (int i = 0; i < NumVertices; i++) {
        float2 edgeA = tex2Dgrad(VertexDataSampler, float2(i * indexMultiplier, 0), 0, 0);
        float2 edgeB = tex2Dgrad(VertexDataSampler, float2((i + 1) * indexMultiplier, 0), 0, 0);

        float2 closest = closestPointOnEdge(vpos, edgeA, edgeB);
        float2 closestDeltaXy = (vpos - closest);
        float deltaMinZ = SliceZ - MinZ;
        float deltaMaxZ = SliceZ - MaxZ;

        float closestDeltaZ;
        if ((SliceZ >= MinZ) && (SliceZ <= MaxZ)) {
            closestDeltaZ = 0;
        } else if (abs(deltaMinZ) > abs(deltaMaxZ)) {
            closestDeltaZ = deltaMaxZ;
        } else {
            closestDeltaZ = deltaMinZ;
        }

        float3 closestDelta = float3(closestDeltaXy.x, closestDeltaXy.y, closestDeltaZ * ZDistanceScale);
        float  closestDistance = length(closestDelta);

        resultDistance = min(resultDistance, closestDistance);
    }

    return resultDistance;
}

void InteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 vpos : VPOS
) {
    float resultDistance;
    if ((SliceZ >= MinZ) && (SliceZ <= MaxZ)) {
        vpos *= DistanceFieldInvScaleFactor;
        vpos += ViewportPosition;
        resultDistance = -computeDistance(vpos);
    } else {
        resultDistance = min(abs(SliceZ - MinZ), abs(SliceZ - MaxZ)) * ZDistanceScale;
    }
    color = encodeDistance(resultDistance);
}

void ExteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 vpos  : VPOS
) {
    vpos *= DistanceFieldInvScaleFactor;
    vpos += ViewportPosition;
    float resultDistance = computeDistance(vpos);
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