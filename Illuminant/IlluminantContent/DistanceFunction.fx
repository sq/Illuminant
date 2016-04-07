#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

uniform float2 PixelSize;
uniform float  SliceZ;

void DistanceFunctionVertexShader(
    in    float3 position : POSITION0, // x, y, z
    inout float3 center   : POSITION1,
    inout float3 size     : POSITION2,
    out   float4 result   : POSITION0
) {
    result = TransformPosition(float4(position.xy - ViewportPosition, 0, 1), 0);
    result.z = position.z;
}

float3 getPosition (in float2 vpos, in float3 center) {
    vpos *= DistanceField.InvScaleFactor;
    vpos += ViewportPosition;
    float3 result = float3(vpos, SliceZ);
    result -= center;
    return result;
}

void BoxPixelShader (
    out float4 color  : COLOR0,
    in  float2 vpos   : VPOS,
    in  float3 center : POSITION1,
    in  float3 size   : POSITION2
) {
    float3 position = getPosition(vpos, center);

    float3 d = abs(position) - size;
    float resultDistance = 
        min(
            max(d.x, max(d.y, d.z)),
            0.0
        ) + length(max(d, 0.0)
    );

    color = encodeDistance(resultDistance);
}

void EllipsoidPixelShader(
    out float4 color  : COLOR0,
    in  float2 vpos   : VPOS,
    in  float3 center : POSITION1,
    in  float3 radius : POSITION2
) {
    float3 position = getPosition(vpos, center);

    float l = length(position / radius) - 1.0;
    float resultDistance =
        l * min(min(radius.x, radius.y), radius.z);

    color = encodeDistance(resultDistance);
}

void CylinderPixelShader(
    out float4 color  : COLOR0,
    in  float2 vpos   : VPOS,
    in  float3 center : POSITION1,
    in  float3 size   : POSITION2
) {
    float3 position = getPosition(vpos, center);

    // HACK: Inigo's formula for this doesn't seem to work, so TIME TO WING IT

    float l = length(position.xy / size.xy) - 1.0;
    float distanceXy = l * min(size.x, size.y);
    float distanceZ = abs(position.z / size.z) - 1.0;

    float resultDistance;
    if ((distanceXy <= 0) && (distanceZ <= 0)) {
        resultDistance = max(distanceXy, distanceZ * size.z);
    } else if (distanceZ <= 0) {
        resultDistance = distanceXy;
    } else {
        resultDistance = distanceZ * size.z;
    }

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