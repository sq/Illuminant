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

float computeDistance (float3 xyz, float2 zRange) {
    float resultDistance = 9999999;
    float indexMultiplier = 1.0 / NumVertices;
    float u = 0;
    int intersectionCount = 0;
    float2 ray = float2(1, 0);

    [loop]
    for (int i = 0; i < NumVertices; i += 1) {
        float2 a, b, temp;
        loadEdge(u, a, b);
            
        if (doesRayIntersectLine(xyz.xy, ray, a, b, temp))
            intersectionCount += 1;

        float2 closest = closestPointOnEdge(xyz.xy, a, b);
        float2 closestDeltaXy = (xyz.xy - closest);
        resultDistance = min(resultDistance, length(closestDeltaXy));

        u += indexMultiplier;
    }

    bool isInside = (xyz.z >= zRange.x) && (xyz.z <= zRange.y) && ((intersectionCount % 2) == 1);
    return isInside ? -resultDistance : resultDistance;
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

float computeEncodedSliceDistance (float2 vpos, float2 zRange, float sliceZ) {
    float distance = computeDistance(float3(vpos, sliceZ), zRange);
    return encodeDistance(distance);
}

void ExteriorPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    in  float2 vpos  : VPOS
) {
    vpos *= getInvScaleFactors();
    vpos += Viewport.Position;

    color = float4(
        computeEncodedSliceDistance(vpos, zRange, SliceZ.x),
        computeEncodedSliceDistance(vpos, zRange, SliceZ.y),
        computeEncodedSliceDistance(vpos, zRange, SliceZ.z),
        computeEncodedSliceDistance(vpos, zRange, SliceZ.w)
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