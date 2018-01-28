#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define OFFSET 1.0

#include "VisualizeCommon.fxh"

static const float3 Normals[] = {
    {-1, 0, 0},
    {1, 0, 0},
    {0, -1, 0},
    {0, 1, 0},
    {0, 0, -1}
};

uniform float Time;

void ProbeSelectorVertexShader (
    inout float4 position      : POSITION0,
    inout float3 probePosition : POSITION1
) {
}

void ProbeSelectorPixelShader(
    in  float3 probePosition : POSITION1,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    int index = floor(vpos.y / 2);
    int subIndex = floor(vpos.y % 2);
    float3 normal = normalize(Normals[index]);

    [branch]
    if (subIndex == 1) {
        result = float4(-normal, 1);
        return;
    }

    // TODO: One fragment per probe and use MRT to output position and normal

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float intersectionDistance;
    float3 estimatedIntersection;

    if (traceSurface(probePosition, normal, intersectionDistance, estimatedIntersection, vars)) {
        result = float4(estimatedIntersection - (normal * OFFSET), 1);
        // float3 normal = estimateNormal(estimatedIntersection, vars);
    } else {
        result = 0;
    }
}

technique ProbeSelector {
    pass P0
    {
        vertexShader = compile vs_3_0 ProbeSelectorVertexShader();
        pixelShader = compile ps_3_0 ProbeSelectorPixelShader();
    }
}