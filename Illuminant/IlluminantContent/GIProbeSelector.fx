#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define OFFSET 1
#define SEARCH_DISTANCE 1024

#include "VisualizeCommon.fxh"

static const float3 Normals[] = {
    {-1, 0, 0}
    ,{1, 0, 0}
    ,{0, -1, 0}
    ,{0, 1, 0}
    ,{-1, -1, 0}
    ,{ 1, 1, 0 }
    ,{ 1, -1, 0 }
    ,{ -1, 1, 0 }
};

uniform float Time;

// uniform float  MaxSearchDistance;
uniform float2 RequestedPositionTexelSize;

Texture2D RequestedPositions       : register(t2);
sampler   RequestedPositionSampler : register(s2) {
    Texture = (RequestedPositions);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void ProbeSelectorVertexShader (
    inout float4 position      : POSITION0
) {
}

void ProbeSelectorPixelShader(
    in  float2 vpos           : VPOS,
    out float4 resultPosition : COLOR0,
    out float4 resultNormal   : COLOR1
) {
    float3 normal = normalize(Normals[vpos.y]);

    float2 uv = vpos * RequestedPositionTexelSize;
    float3 requestedPosition = tex2Dlod(RequestedPositionSampler, float4(uv, 0, 0)).xyz;

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float initialDistance = sampleDistanceField(requestedPosition, vars);

    [branch]
    if (initialDistance <= 1) {
        resultPosition = 0;
        resultNormal = 0;
        return;
    }

    float intersectionDistance;
    float3 estimatedIntersection;
    float3 ray = normal * SEARCH_DISTANCE;

    if (traceSurface(requestedPosition, ray, intersectionDistance, estimatedIntersection, vars)) {
        resultPosition = float4(estimatedIntersection - (normal * OFFSET), 1);
        resultNormal = float4(-normal, 1);
    } else {
        resultPosition = 0;
        resultNormal = 0;
    }
}

technique ProbeSelector {
    pass P0
    {
        vertexShader = compile vs_3_0 ProbeSelectorVertexShader();
        pixelShader = compile ps_3_0 ProbeSelectorPixelShader();
    }
}