#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "Common.fxh"

#define MAX_VERTICES   40

uniform float2 PixelSize;
uniform float2 Vertices[MAX_VERTICES];
uniform float2 CoordinateOffset, CoordinateScale;
uniform float  DistanceLimit;
uniform int    NumVertices;

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

        float2 closest = closestPointOnEdge(self, edgeA, edgeB);
        float2 closestDelta = self - closest;
        float  closestDistance = length(closestDelta);

        if (closestDistance < resultPointDistance) {
            resultPoint = closest;
            resultPointDistance = closestDistance;
        }
    }

    if (resultPointDistance >= DistanceLimit)
        discard;

    depth = resultPointDistance / DistanceLimit;
    float2 adjustedResult = (resultPoint + CoordinateOffset) * CoordinateScale;

    color = float4(
        adjustedResult.x, adjustedResult.y, 0, 1
    );
}

technique Edge
{
    pass P0
    {
        vertexShader = compile vs_3_0 EdgeVertexShader();
        pixelShader  = compile ps_3_0 EdgePixelShader();
    }
}