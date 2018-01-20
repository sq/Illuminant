#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

uniform float2 PixelSize;
uniform float4 SliceZ;

void DistanceFunctionVertexShader(
    in    float3 position : POSITION0, // x, y, z
    inout float3 center   : TEXCOORD0,
    inout float3 size     : TEXCOORD1,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - Viewport.Position, 0, 1), 0);
    result.z = position.z;
}

float2 getPositionXy (in float2 vpos) {
    vpos *= getInvScaleFactor();
    vpos += Viewport.Position;
    return vpos;
}

void BoxPixelShader (
    out float4 color  : COLOR0,
    in  float2 vpos   : VPOS,
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
    in  float2 vpos   : VPOS,
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
    in  float2 vpos   : VPOS,
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