// IEEE strictness prevents some math errors with certain polygons' distance fields
#pragma fxcparams(/O3 /Gis /Ges)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\SDF2D.fxh"

// HACK: Contract the polygon's boundaries a little bit to mitigate acne on inexact polygons like circles
#define PolygonXyBias 1.5

uniform const float2 PixelSize;
uniform const float4 SliceZ;
uniform const float4 Bounds;
// Start, Step
uniform const float4 Uv;

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
    result = TransformPosition(float4(position.xy - GetViewportPosition(), 0, 1), 0);
    result.z = 0;
}

void loadEdge (float4 uv, out float2 a, out float2 b) {
    float4 packedEdge = tex2Dlod(VertexDataSampler, uv);
    a = packedEdge.xy;
    b = packedEdge.zw;
}

float computeDistanceZ (float sliceZ, float2 zRange) {
    if (sliceZ >= zRange.x) {
        if (sliceZ <= zRange.y)
            return max(sliceZ - zRange.y, zRange.x - sliceZ);
        else
            return sliceZ - zRange.y;
    } else
        return zRange.x - sliceZ;
}

float finalEval (float z, float2 zRange, float resultDistanceSq, float sign) {
    float distanceZ = computeDistanceZ(z, zRange);
    float distanceXy = (sqrt(resultDistanceSq) * sign) + PolygonXyBias;
    if (distanceXy <= 0) {
        if (distanceZ <= 0) {
            // Inside on all 3 axes, so sum the distances
            return distanceXy + distanceZ;
        } else {
            // Inside on xy axes but not z. Just return the z distance (is this wrong?)
            return distanceZ;
        }
    } else {
        // Outside on xy axes, so never return a negative distance even if inside on z axis,
        //  but increase distance if also outside on z axis
        return max(distanceXy, 0) + max(distanceZ, 0);
    }
}

float4 computeSliceDistances (float2 xy, float2 zRange, float4 SliceZ) {
    float resultDistanceSq = 999999;
    float4 uv = float4(Uv.x, Uv.y, 0, 0);
    bool badDown = false, badRight = false;
    
    float polyDistance, polySign;
    float2 a, b;
    loadEdge(0, a, b);
    sdPolygonInit(xy, b, a, polyDistance, polySign);

    uv.xy += Uv.zw;
    [loop]
    while (uv.x < 1) {
        loadEdge(uv, a, b);
        sdPolygonVertex(xy, b, a, polyDistance, polySign);
        uv.xy += Uv.zw;
    }

    float4 result = float4(
        finalEval(SliceZ.x, zRange, polyDistance, polySign),
        finalEval(SliceZ.y, zRange, polyDistance, polySign),
        finalEval(SliceZ.z, zRange, polyDistance, polySign),
        finalEval(SliceZ.w, zRange, polyDistance, polySign)
    );
    return result;
}

void DistanceToPolygonPixelShader (
    out float4 color : COLOR0,
    in  float2 zRange : TEXCOORD0,
    ACCEPTS_VPOS
) {
    float2 vp = (__vpos__ * getInvScaleFactors()) + GetViewportPosition();

    float4 sliceDistances = computeSliceDistances(vp, zRange, SliceZ);
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