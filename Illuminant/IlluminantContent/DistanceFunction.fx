#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define MAX_DISTANCE_OBJECTS 16

#define TYPE_SPHERE 0
#define TYPE_BOX    1

uniform float2 PixelSize;
uniform float  MinZ, MaxZ;
uniform float  SliceZ;

uniform int    NumDistanceObjects;

uniform float  DistanceObjectTypes[MAX_DISTANCE_OBJECTS];
uniform float3 DistanceObjectCenters[MAX_DISTANCE_OBJECTS];
uniform float3 DistanceObjectSizes[MAX_DISTANCE_OBJECTS];

void DistanceFunctionVertexShader(
    in    float3 position      : POSITION0, // x, y, z
    out   float4 result : POSITION0
) {
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = position.z;
}

float evaluateFunction (
    float3 position,
    float type, float3 center, float3 size
) {
    position -= center;
    type = floor(type);

    if (type == TYPE_SPHERE) {
        return length(position) - size;
    } else if (type == TYPE_BOX) {
        float3 d = abs(position) - size;
        return
            min(
                max(d.x, max(d.y, d.z)),
                0.0
                ) + length(
                    max(d, 0.0)
                    );
    }

    // FIXME
    return 0;
}

float computeDistance (
    float2 vpos
) {
    float3 worldPosition = float3(vpos.x, vpos.y, SliceZ);
    float resultDistance = 99999;

    [loop]
    for (int i = 0; i < NumDistanceObjects; i++) {
        resultDistance = min(resultDistance, evaluateFunction(
            worldPosition,
            DistanceObjectTypes[i], DistanceObjectCenters[i], DistanceObjectSizes[i]
        ));
    }

    return resultDistance;
}

void DistanceFunctionPixelShader (
    out float4 color : COLOR0,
    in  float2 vpos  : VPOS
) {
    vpos *= DistanceFieldInvScaleFactor;
    vpos += ViewportPosition;

    float resultDistance = computeDistance(vpos);
    color = encodeDistance(resultDistance);
}

technique DistanceFunction
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceFunctionVertexShader();
        pixelShader = compile ps_3_0 DistanceFunctionPixelShader();
    }
}