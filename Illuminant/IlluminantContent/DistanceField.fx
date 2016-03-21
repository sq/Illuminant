#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\BitmapCommon.fxh"
#include "Common.fxh"

#define MAX_VERTICES   44

uniform float2 PixelSize;
uniform float2 Vertices[MAX_VERTICES];
uniform int    NumVertices;

void DistanceVertexShader (
    in    float3 position      : POSITION0, // x, y, z
    out   float4 result        : POSITION0
) {
    result = TransformPosition(float4(position.xy, 0, 1), 0);
    result.z = position.z;
}

float computeDistance (
    float2 vpos
) {
    float resultDistance = 99999;

    for (int i = 0; i < NumVertices; i++) {
        float2 edgeA = Vertices[i];
        float2 edgeB = Vertices[i + 1];

        if (i >= NumVertices - 1)
            edgeB = Vertices[0];

        float2 closest = closestPointOnEdge(vpos, edgeA, edgeB);
        float2 closestDelta = vpos - closest;
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
    float resultDistance = -computeDistance(vpos);
    color = encodeDistance(resultDistance);
    depth = distanceToDepth(resultDistance);
}

void ExteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 vpos  : VPOS,
    out float  depth : DEPTH
) {
    float resultDistance = computeDistance(vpos);
    color = encodeDistance(resultDistance);
    depth = distanceToDepth(resultDistance);
}

void VisualizePixelShader(
    in float4 multiplyColor : COLOR0,
    in float4 addColor : COLOR1,
    in float2 texCoord : TEXCOORD0,
    in float2 texTL : TEXCOORD1,
    in float2 texBR : TEXCOORD2,
    out float4 result : COLOR0
) {
    addColor.rgb *= addColor.a;
    addColor.a = 0;

    float4 encoded = tex2D(TextureSampler, clamp(texCoord, texTL, texBR));
    float decoded = decodeDistance(encoded);

    float r = 1.0 - clamp(decoded / 256, 0, 1);
    float g = abs(min(0, decoded / 16));

    float4 visualized = float4(r, g, 0, 1);

    result = multiplyColor * visualized;
    result += (addColor * result.a);
}

technique Visualize
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 VisualizePixelShader();
    }
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