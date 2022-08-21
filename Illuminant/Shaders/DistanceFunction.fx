#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

// HACK: For some reason we need to expand the function boxes for things to work right?
#define FUNCTION_SIZE_HACK 1

uniform const float2 PixelSize;
uniform const float4 SliceZ;

void DistanceFunctionVertexShader(
    in    float3 cornerWeights       : NORMAL2,
    inout float3 center              : TEXCOORD0,
    inout float3 size                : TEXCOORD1,
    inout float  rotation            : TEXCOORD2,
    out   float4 result              : POSITION0
) {
    float msize = max(max(abs(size.x), abs(size.y)), abs(size.z)) + getMaximumEncodedDistance() + 4;
    float2 position = ((cornerWeights.xy * 2 - 1) * (msize * FUNCTION_SIZE_HACK)) + center.xy;
    result = TransformPosition(float4(position - GetViewportPosition(), 0, 1), 0);
    result.z = 0;
    result.w = 1;
}

float2 getPositionXy (in float2 __vpos__) {
    float2 vp = (__vpos__ * getInvScaleFactors()) + GetViewportPosition();
    return vp;
}

void BoxPixelShader (
    out float4 color  : COLOR0,
    ACCEPTS_VPOS,
    in  float3 center   : TEXCOORD0,
    in  float3 size     : TEXCOORD1,
    in  float  rotation : TEXCOORD2
) {
    float2 vpos = GET_VPOS;
    color = float4(
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.x), center, size, rotation)),
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.y), center, size, rotation)),
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.z), center, size, rotation)),
        encodeDistance(evaluateBox(float3(getPositionXy(vpos), SliceZ.w), center, size, rotation))
    );
}

void EllipsoidPixelShader(
    out float4 color  : COLOR0,
    ACCEPTS_VPOS,
    in  float3 center   : TEXCOORD0,
    in  float3 size     : TEXCOORD1,
    in  float  rotation : TEXCOORD2
) {
    float2 vpos = GET_VPOS;
    color = float4(
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.x), center, size, rotation)),
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.y), center, size, rotation)),
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.z), center, size, rotation)),
        encodeDistance(evaluateEllipsoid(float3(getPositionXy(vpos), SliceZ.w), center, size, rotation))
    );
}

void CylinderPixelShader(
    out float4 color  : COLOR0,
    ACCEPTS_VPOS,
    in  float3 center   : TEXCOORD0,
    in  float3 size     : TEXCOORD1,
    in  float  rotation : TEXCOORD2
) {
    float2 vpos = GET_VPOS;
    color = float4(
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.x), center, size, rotation)),
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.y), center, size, rotation)),
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.z), center, size, rotation)),
        encodeDistance(evaluateCylinder(float3(getPositionXy(vpos), SliceZ.w), center, size, rotation))
    );
}

void SpheroidPixelShader(
    out float4 color  : COLOR0,
    ACCEPTS_VPOS,
    in  float3 center   : TEXCOORD0,
    in  float3 size     : TEXCOORD1,
    in  float  rotation : TEXCOORD2
) {
    float2 vpos = GET_VPOS;
    color = float4(
        encodeDistance(evaluateSpheroid(float3(getPositionXy(vpos), SliceZ.x), center, size, rotation)),
        encodeDistance(evaluateSpheroid(float3(getPositionXy(vpos), SliceZ.y), center, size, rotation)),
        encodeDistance(evaluateSpheroid(float3(getPositionXy(vpos), SliceZ.z), center, size, rotation)),
        encodeDistance(evaluateSpheroid(float3(getPositionXy(vpos), SliceZ.w), center, size, rotation))
    );
}

void OctagonPixelShader(
    out float4 color  : COLOR0,
    ACCEPTS_VPOS,
    in  float3 center   : TEXCOORD0,
    in  float3 size     : TEXCOORD1,
    in  float  rotation : TEXCOORD2
) {
    float2 vpos = GET_VPOS;
    color = float4(
        encodeDistance(evaluateOctagon(float3(getPositionXy(vpos), SliceZ.x), center, size, rotation)),
        encodeDistance(evaluateOctagon(float3(getPositionXy(vpos), SliceZ.y), center, size, rotation)),
        encodeDistance(evaluateOctagon(float3(getPositionXy(vpos), SliceZ.z), center, size, rotation)),
        encodeDistance(evaluateOctagon(float3(getPositionXy(vpos), SliceZ.w), center, size, rotation))
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

technique Spheroid
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceFunctionVertexShader();
        pixelShader = compile ps_3_0 SpheroidPixelShader();
    }
}

technique Octagon
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceFunctionVertexShader();
        pixelShader = compile ps_3_0 OctagonPixelShader();
    }
}