#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

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
    inout float  rotation : TEXCOORD2,
    inout int2   typeId   : BLENDINDICES1,
    out   float4 result   : POSITION0
) {
    // FIXME: Is this right when the shape is rotated?
    float msize = max(max(abs(size.x), abs(size.y)), abs(size.z)) + getMaximumEncodedDistance() + 3;
    float2 position = (FunctionCorners[cornerIndex.x] * msize) + center.xy;
    result = TransformPosition(float4(position - GetViewportPosition(), 0, 1), 0);
    result.z = 0;
    result.w = 1;
}

float2 getPositionXy (in float2 __vpos__) {
    float2 vp = (__vpos__ * getInvScaleFactors()) + GetViewportPosition();
    return vp;
}

void DistanceFunctionPixelShader (
    out float4 color  : COLOR0,
    ACCEPTS_VPOS,
    in  float3 center   : TEXCOORD0,
    in  float3 size     : TEXCOORD1,
    in  float  rotation : TEXCOORD2,
    in  int2   typeId   : BLENDINDICES1
) {
    float2 vpos = GET_VPOS;
    float2 positionXy = getPositionXy(vpos);
    color = float4(
        encodeDistance(evaluateByTypeId(typeId.x, float3(positionXy, SliceZ.x), center, size, rotation)),
        encodeDistance(evaluateByTypeId(typeId.x, float3(positionXy, SliceZ.y), center, size, rotation)),
        encodeDistance(evaluateByTypeId(typeId.x, float3(positionXy, SliceZ.z), center, size, rotation)),
        encodeDistance(evaluateByTypeId(typeId.x, float3(positionXy, SliceZ.w), center, size, rotation))
    );
}

technique DistanceFunction
{
    pass P0
    {
        vertexShader = compile vs_3_0 DistanceFunctionVertexShader();
        pixelShader  = compile ps_3_0 DistanceFunctionPixelShader();
    }
}