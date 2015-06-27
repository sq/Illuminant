#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"

#define MAX_VERTICES   40
#define DISTANCE_LIMIT 128

uniform float2 PixelSize;
uniform float2 Vertices[MAX_VERTICES];
uniform int    NumVertices;

float2 ClosestPointOnEdge (
    float2 pt, float2 edgeStart, float2 edgeEnd
) {
    float2 edgeDelta = edgeEnd - edgeStart;
    float  edgeLength = length(edgeDelta);
    edgeLength *= edgeLength;

    float2 pointDelta = (pt - edgeStart) * edgeDelta;
    float u = (pointDelta.x + pointDelta.y) / edgeLength;

    return edgeStart + (edgeDelta * clamp(u, 0, 1));
}

void EdgeVertexShader (
    in    float3 position      : POSITION0, // x, y, z
    out   float4 result        : POSITION0
) {
    result = TransformPosition(float4(position.xy, 0, 1), 0);
    result.z = position.z;
}

void EdgePixelShader (
    inout float4 color : COLOR0,
    in    float2 vpos  : VPOS,
    out   float  depth : DEPTH
) {
    float2 resultPoint         = float2(0, 0);
    float  resultPointDistance = 99999;

    float2 self = vpos;

    for (int i = 0; i < NumVertices; i++) {
        float2 edgeA = Vertices[i];
        float2 edgeB = Vertices[i + 1];

        if (i == NumVertices - 1)
            edgeB = Vertices[0];

        float2 closest = ClosestPointOnEdge(self, edgeA, edgeB);
        float2 closestDelta = self - closest;
        float  closestDistance = length(closestDelta);

        if (closestDistance < resultPointDistance) {
            resultPoint = closest;
            resultPointDistance = closestDistance;
        }
    }

    if (resultPointDistance >= DISTANCE_LIMIT)
        discard;

    depth = resultPointDistance / DISTANCE_LIMIT;
    if (false)
        color = float4(
            resultPoint.x / 512,
            resultPoint.y / 512,
            0, 1
        );
    else
        color = float4(depth, depth, depth, 1);
}

technique Edge
{
    pass P0
    {
        vertexShader = compile vs_3_0 EdgeVertexShader();
        pixelShader  = compile ps_3_0 EdgePixelShader();
    }
}