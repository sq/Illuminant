#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

uniform float2 PixelSize;
uniform float4 SliceZ;
uniform int    NumVertices;

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

void loadEdge (float u, out float2 a, out float2 b) {
    float4 packedEdge = tex2Dlod(VertexDataSampler, float4(u, 0, 0, 0));
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

void computeDistanceStep (float2 xy, inout float resultDistanceSq, inout int intersectionCount, in float u) {
    float2 a, b, temp;
    loadEdge(u, a, b);
    intersectionCount += doesRightRayIntersectLine(xy, a, b) ? 1 : 0;
    float2 closest = closestPointOnEdge(xy, a, b);
    float2 closestDeltaXy = (xy - closest);
    closestDeltaXy *= closestDeltaXy;
    resultDistanceSq = min(resultDistanceSq, (closestDeltaXy.x + closestDeltaXy.y));
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
    float resultDistanceSq = 99999999;
    float indexMultiplier = 1.0 / NumVertices;
    float u = 0;
    int intersectionCount = 0;

    [loop]
    for (int i = 0; i < NumVertices; i += 1) {
        computeDistanceStep(xy, resultDistanceSq, intersectionCount, u);
        u += indexMultiplier;
    }

    float4 result = float4(
        finalEval(SliceZ.x, zRange, resultDistanceSq, intersectionCount),
        finalEval(SliceZ.y, zRange, resultDistanceSq, intersectionCount),
        finalEval(SliceZ.z, zRange, resultDistanceSq, intersectionCount),
        finalEval(SliceZ.w, zRange, resultDistanceSq, intersectionCount)
    );
    return result;
}

/*

float computeDistanceToEdge (
    float2 vpos, float u
) {
    float2 edge = edgeB - edgeA;

    float2 edgeLeft = float2(edge.y, -edge.x);

    float2 closest = closestPointOnEdge(vpos, edgeA, edgeB);
    float2 closestDeltaXy = (vpos - closest);
    float inside = dot(closestDeltaXy, normalize(edgeLeft));

    return length(closestDeltaXy) * sign(inside);
}

float computeDistanceXy (
    float2 vpos
) {
    float resultDistance = 99999999;
    float indexMultiplier = 1.0 / NumVertices;
    float u = 0;

    [loop]
    for (int i = 0; i < NumVertices; i += 6) {
        // fxc can't handle unrolling loops without spending 30 minutes, yaaaaaaaaaay
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultDistance = min(resultDistance, computeDistanceToEdge(vpos, u));
        u += indexMultiplier;
    }

    return resultDistance;
}

float computeInteriorDistance (
    float2 zRange,
    float squaredDistanceXy,
    float sliceZ
) {
    return squaredDistanceXy;
    if ((sliceZ >= zRange.x) && (sliceZ <= zRange.y)) {
        float squaredDistanceZ = computeSquaredDistanceZ(sliceZ, zRange);
        return -sqrt(squaredDistanceXy + squaredDistanceZ);
    } else {
        return min(abs(sliceZ - zRange.x), abs(sliceZ - zRange.y));
    }
}

float computeExteriorDistance (
    float2 zRange,
    float squaredDistanceXy,
    float sliceZ
) {
    return squaredDistanceXy;
    float squaredDistanceZ = computeSquaredDistanceZ(sliceZ, zRange);
    return sqrt(squaredDistanceXy + squaredDistanceZ);
}

*/

void InteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    in  float2 vpos : VPOS
) {
    vpos *= getInvScaleFactors();
    vpos += Viewport.Position;
    color = 0;
    
    /*
    float squaredDistanceXy = computeDistanceXy(vpos);

    color = float4(
        encodeDistance(computeInteriorDistance(zRange, squaredDistanceXy, SliceZ.x)),
        encodeDistance(computeInteriorDistance(zRange, squaredDistanceXy, SliceZ.y)),
        encodeDistance(computeInteriorDistance(zRange, squaredDistanceXy, SliceZ.z)),
        encodeDistance(computeInteriorDistance(zRange, squaredDistanceXy, SliceZ.w))
    );
    */
}

void ExteriorPixelShader (
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