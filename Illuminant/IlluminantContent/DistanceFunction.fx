#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

uniform float2 PixelSize;
uniform float  SliceZ;

void DistanceFunctionVertexShader(
    in    float3 position : POSITION0, // x, y, z
    inout float3 center   : TEXCOORD0,
    inout float3 size     : TEXCOORD1,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - Viewport.Position, 0, 1), 0);
    result.z = position.z;
}

float3 getPosition (in float2 vpos, float sliceZ) {
    vpos *= DistanceField.InvScaleFactor;
    vpos += Viewport.Position;
    return float3(vpos, sliceZ);
}

void BoxPixelShader (
    out float4 color  : COLOR0,
    in  float2 vpos   : VPOS,
    in  float3 center : TEXCOORD0,
    in  float3 size   : TEXCOORD1
) {
    float resultDistance = evaluateBox(getPosition(vpos, SliceZ), center, size);
    color = encodeDistance(resultDistance);
}

void EllipsoidPixelShader(
    out float4 color  : COLOR0,
    in  float2 vpos   : VPOS,
    in  float3 center : TEXCOORD0,
    in  float3 size : TEXCOORD1
) {
    float resultDistance = evaluateEllipsoid(getPosition(vpos, SliceZ), center, size);
    color = encodeDistance(resultDistance);
}

void CylinderPixelShader(
    out float4 color  : COLOR0,
    in  float2 vpos   : VPOS,
    in  float3 center : TEXCOORD0,
    in  float3 size : TEXCOORD1
) {
    float resultDistance = evaluateCylinder(getPosition(vpos, SliceZ), center, size);
    color = encodeDistance(resultDistance);
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