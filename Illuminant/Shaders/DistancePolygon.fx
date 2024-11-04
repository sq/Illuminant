// IEEE strictness prevents some math errors with certain polygons' distance fields
#pragma fxcparams(/O3 /Gis /Ges)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\SDF2D.fxh"
#include "DistanceFunctionCommon.fxh"

#define MAX_VERTICES 32

uniform const int    VertexCount;
uniform const float4 Vertices[MAX_VERTICES];
uniform const float2 MinAndMaxZ;

uniform const float2 PixelSize;
uniform const float4 SliceZ;

void DistancePolygonVertexShader(
    in    float3 cornerWeights : NORMAL2,
    out   float4 result        : POSITION0
) {
    // FIXME: Compute accurate size
    // float msize = max(max(abs(size.x), abs(size.y)), abs(size.z)) + getMaximumEncodedDistance() + 0.5;
    float msize = 99999;
    float2 position = ((cornerWeights.xy * 2 - 1) * msize);
    result = TransformPosition(float4(position - GetViewportPosition(), 0, 1), 0);
    result.z = 0;
    result.w = 1;
}

float computeSquaredDistanceZ (float sliceZ, float2 zRange) {
    float deltaMinZ = sliceZ - zRange.x;
    float deltaMaxZ = sliceZ - zRange.y;

    if ((sliceZ >= zRange.x) && (sliceZ <= zRange.y)) {
        return 0;
    } else if (abs(deltaMinZ) > abs(deltaMaxZ)) {
        return deltaMaxZ * deltaMaxZ;
    } else {
        return deltaMinZ * deltaMinZ;
    }
}

float2 getPositionXy (in float2 __vpos__) {
    float2 vp = (__vpos__ * getInvScaleFactors()) + GetViewportPosition();
    return vp;
}

float evaluatePolygon (float3 worldPosition) {
    return worldPosition.x / 1024;
}

void PolygonPixelShader (
    out float4 color  : COLOR0,
    ACCEPTS_VPOS
) {
    float2 vpos = GET_VPOS;
    float2 xy = getPositionXy(vpos);
    color = float4(
        encodeDistance(evaluatePolygon(float3(xy, SliceZ.x))),
        encodeDistance(evaluatePolygon(float3(xy, SliceZ.y))),
        encodeDistance(evaluatePolygon(float3(xy, SliceZ.z))),
        encodeDistance(evaluatePolygon(float3(xy, SliceZ.w)))
    );
}

technique Cylinder
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistancePolygonVertexShader();
        pixelShader  = compile ps_3_0 PolygonPixelShader();
    }
}