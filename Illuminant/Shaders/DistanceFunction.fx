#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

// HACK: For some reason we need to expand the function boxes for things to work right?
#define FUNCTION_SIZE_HACK 1.1

uniform float2 PixelSize;
uniform float4 SliceZ;

static const float2 FunctionCorners[] = {
    { -1, -1 },
    { 1, -1 },
    { 1, 1 },
    { -1, 1 }
};

void DistanceFunctionVertexShader(
    in int2 cornerIndex   : BLENDINDICES0,
    inout float3 center   : TEXCOORD0,
    inout float3 size     : TEXCOORD1,
    out   float4 result   : POSITION0
) {
    float msize = max(max(abs(size.x), abs(size.y)), abs(size.z)) + getMaximumEncodedDistance() + 0.5;
    float2 position = (FunctionCorners[cornerIndex.x] * (msize * FUNCTION_SIZE_HACK)) + center.xy;
    result = TransformPosition(float4(position - Viewport.Position, 0, 1), 0);
    result.z = 0;
    result.w = 1;
}

float2 getPositionXy (in float2 __vpos__) {
    float2 vp = (__vpos__ * getInvScaleFactors()) + Viewport.Position;
    return vp;
}

void BoxPixelShader (
    out float4 color  : COLOR0,
    in  float2 __vpos__   : VPOS,
    in  float3 center : TEXCOORD0,
    in  float3 size   : TEXCOORD1
) {
    color = float4(
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.x), center, size)),
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.y), center, size)),
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.z), center, size)),
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.w), center, size))
    );
}

void EllipsoidPixelShader(
    out float4 color  : COLOR0,
    in  float2 __vpos__   : VPOS,
    in  float3 center : TEXCOORD0,
    in  float3 size : TEXCOORD1
) {
    color = float4(
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.x), center, size)),
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.y), center, size)),
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.z), center, size)),
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.w), center, size))
    );
}

void CylinderPixelShader(
    out float4 color  : COLOR0,
    in  float2 __vpos__   : VPOS,
    in  float3 center : TEXCOORD0,
    in  float3 size : TEXCOORD1
) {
    color = float4(
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.x), center, size)),
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.y), center, size)),
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.z), center, size)),
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.w), center, size))
    );
}

technique Box
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceFunctionVertexShader();
        pixelShader  = compile ps_3_0 BoxPixelShader();
    }
}

technique Ellipsoid
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceFunctionVertexShader();
        pixelShader  = compile ps_3_0 EllipsoidPixelShader();
    }
}

technique Cylinder
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceFunctionVertexShader();
        pixelShader  = compile ps_3_0 CylinderPixelShader();
    }
}