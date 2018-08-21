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
    inout float3 zRange   : TEXCOORD0,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - Viewport.Position, 0, 1), 0);
    result.z = 0;
}

float computeSquaredDistanceToEdge (
    float2 vpos, float u
) {
    float4 packedEdge = tex2Dlod(VertexDataSampler, float4(u, 0, 0, 0));
    float2 edgeA = packedEdge.xy;
    float2 edgeB = packedEdge.zw;

    float2 closest = closestPointOnEdge(vpos, edgeA, edgeB);
    float2 closestDeltaXy = (vpos - closest);
    closestDeltaXy *= closestDeltaXy;

    return closestDeltaXy.x + closestDeltaXy.y;
}

float computeSquaredDistanceXy (
    float2 vpos
) {
    float resultSquaredDistance = 99999999;
    float indexMultiplier = 1.0 / NumVertices;
    float u = 0;

    [loop]
    for (int i = 0; i < NumVertices; i += 4) {
        // fxc can't handle unrolling loops without spending 30 minutes, yaaaaaaaaaay
        resultSquaredDistance = min(resultSquaredDistance, computeSquaredDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultSquaredDistance = min(resultSquaredDistance, computeSquaredDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultSquaredDistance = min(resultSquaredDistance, computeSquaredDistanceToEdge(vpos, u));
        u += indexMultiplier;
        resultSquaredDistance = min(resultSquaredDistance, computeSquaredDistanceToEdge(vpos, u));
        u += indexMultiplier;
    }

    return resultSquaredDistance;
}

float computeSquaredDistanceZ (float sliceZ, float2 zRange) {
    float deltaMinZ = sliceZ - zRange.x;
    float deltaMaxZ = sliceZ - zRange.y;

    if ((sliceZ >= zRange.x) && (sliceZ <= zRange.y)) {
        // FIXME: Should this actually be zero?
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
    float squaredDistanceZ = computeSquaredDistanceZ(sliceZ, zRange);
    return sqrt(squaredDistanceXy + squaredDistanceZ);
}

void InteriorPixelShader (
    out float4 color : COLOR0,
    in  float3 zRange : TEXCOORD0,
    in  float2 vpos : VPOS
) {
    vpos *= getInvScaleFactors();
    vpos += Viewport.Position;
    
    float squaredDistanceXy = computeSquaredDistanceXy(vpos);

    color = float4(
        encodeDistance(computeInteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.x)),
        encodeDistance(computeInteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.y)),
        encodeDistance(computeInteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.z)),
        encodeDistance(computeInteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.w))
    );
}

void ExteriorPixelShader (
    out float4 color : COLOR0,
    in  float3 zRange : TEXCOORD0,
    in  float2 vpos  : VPOS
) {
    vpos *= getInvScaleFactors();
    vpos += Viewport.Position;

    float squaredDistanceXy = computeSquaredDistanceXy(vpos);

    color = float4(
        encodeDistance(computeExteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.x)),
        encodeDistance(computeExteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.y)),
        encodeDistance(computeExteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.z)),
        encodeDistance(computeExteriorDistance(zRange.xy, squaredDistanceXy, SliceZ.w))
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