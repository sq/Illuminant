#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define MAX_VERTICES   40

uniform float2 PixelSize;
uniform float2 Vertices[MAX_VERTICES];
uniform float2 MinZ, MaxZ;
uniform float  SliceZ;
uniform int    NumVertices;

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

    for (int i = 0; i < NumVertices; i++) {
        int i2 = (i >= NumVertices - 1) ? 0 : i + 1;
        float2 edgeA = Vertices[i], edgeB = Vertices[i2];

        float2 closest = closestPointOnEdge(vpos, edgeA, edgeB);
        float2 closestDeltaXy = (vpos - closest);
        float  closestDeltaZ = min(abs(SliceZ - MinZ), abs(SliceZ - MaxZ));
        float3 closestDelta = float3(closestDeltaXy.x, closestDeltaXy.y, closestDeltaZ);
        float  closestDistance = length(closestDelta);

        resultDistance = min(resultDistance, closestDistance);
    }

    return resultDistance;
}

void InteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float  depth : DEPTH
) {
    vpos += ViewportPosition;
    vpos *= DistanceFieldInvScaleFactor;
    float resultDistance = -computeDistance(vpos);
    color = encodeDistance(resultDistance);
    depth = distanceToDepth(resultDistance);
}

void ExteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 vpos  : VPOS,
    out float  depth : DEPTH
) {
    vpos += ViewportPosition;
    vpos *= DistanceFieldInvScaleFactor;
    float resultDistance = computeDistance(vpos);
    color = encodeDistance(resultDistance);
    depth = distanceToDepth(resultDistance);
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