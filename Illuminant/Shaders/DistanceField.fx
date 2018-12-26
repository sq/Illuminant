#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

uniform float2 PixelSize;
uniform float4 SliceZ;
uniform float4 Bounds;
// Start, Step
uniform float4 Uv;

Texture2D VertexDataTexture : register(t0);
sampler   VertexDataSampler : register(s0) {
    Texture = (VertexDataTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU  = CLAMP;
    AddressV  = CLAMP;
};

void DistanceVertexShader (
    in    float3 position : POSITION0, // x, y, z
    inout float2 zRange   : TEXCOORD0,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - Viewport.Position, 0, 1), 0);
    result.z = 0;
}

void loadEdge (float4 uv, out float2 a, out float2 b) {
    float4 packedEdge = tex2Dlod(VertexDataSampler, uv);
    a = packedEdge.xy;
    b = packedEdge.zw;
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

void computeDistanceStep (float2 xy, inout float resultDistanceSq, inout int intersectionCount, in float4 uv) {
    float2 a, b, temp;
    loadEdge(uv, a, b);
    if (doesRightRayIntersectLine(xy, a, b))
        intersectionCount += 1;
    float distanceSq = distanceSquaredToEdge(xy, a, b);
    resultDistanceSq = min(resultDistanceSq, distanceSq);
}

float finalEval (float2 z, float2 zRange, float resultDistanceSq, int intersectionCount) {
    float distanceZSq = computeSquaredDistanceZ(z, zRange);
    float sqrtDistance = sqrt(resultDistanceSq + distanceZSq);
    float aboveBelow = min(abs(z - zRange.x), abs(z - zRange.y));

    bool isInsideZ = (z >= zRange.x) && (z <= zRange.y);
    bool isInsideXy = (intersectionCount % 2) == 1;

    if (isInsideXy) {
        if (isInsideZ)
            return -sqrtDistance;
        else
            return aboveBelow;
    } else {
        return sqrtDistance;
    }
}

float4 computeSliceDistances (float2 xy, float2 zRange, float4 SliceZ) {
    float resultDistanceSq = 999999;
    float4 uv = float4(Uv.x, Uv.y, 0, 0);
    int intersectionCount = 0;

    [loop]
    while (uv.x <= 1) {
        computeDistanceStep(xy, resultDistanceSq, intersectionCount, uv);
        uv.xy += Uv.zw;
    }

    if (
        (xy.x < Bounds.x) ||
        (xy.y < Bounds.y) ||
        (xy.x > Bounds.z) ||
        (xy.y > Bounds.w)
    )
        intersectionCount = 0;

    float4 result = float4(
        finalEval(SliceZ.x, zRange, resultDistanceSq, intersectionCount),
        finalEval(SliceZ.y, zRange, resultDistanceSq, intersectionCount),
        finalEval(SliceZ.z, zRange, resultDistanceSq, intersectionCount),
        finalEval(SliceZ.w, zRange, resultDistanceSq, intersectionCount)
    );
    return result;
}

void DistanceToPolygonPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    in  float2 vpos  : VPOS
) {
    vpos *= getInvScaleFactors();
    vpos += Viewport.Position;

    float4 sliceDistances = computeSliceDistances(vpos, zRange, SliceZ);
    color = float4(
        encodeDistance(sliceDistances.x),
        encodeDistance(sliceDistances.y),
        encodeDistance(sliceDistances.z),
        encodeDistance(sliceDistances.w)
    );
}

technique DistanceToPolygon
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceVertexShader();
        pixelShader  = compile ps_3_0 DistanceToPolygonPixelShader();
    }
}