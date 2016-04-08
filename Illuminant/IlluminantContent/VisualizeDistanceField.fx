#include "DistanceFieldCommon.fxh"

#define MIN_STEP_SIZE 1
#define EPSILON 0.5

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float Time;

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(ViewportPosition.xy, 0)) * float3(ViewportScale, 1));
    return mul(mul(float4(localPosition.xyz, 1), ModelViewMatrix), ProjectionMatrix);
}

void VisualizeVertexShader(
    in float3 position       : POSITION0,
    inout float3 rayStart    : POSITION1,
    inout float3 rayVector   : POSITION2,
    out float3 worldPosition : TEXCOORD2,
    out float4 result        : POSITION0
) {
    worldPosition = position;
    float4 transformedPosition = ApplyTransform(position);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

bool traceRay(
    float3    rayStart,
    float3    rayVector,
    out float intersectionDistance
) {
    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float positionAlongRay = 0;
    float rayLength = length(rayVector);
    float3 rayDirection = rayVector / rayLength;
    intersectionDistance = -1;

    [loop]
    while (positionAlongRay <= rayLength) {
        float3 samplePosition = rayStart + (rayDirection * positionAlongRay);
        float distance = sampleDistanceField(samplePosition, vars);

        [branch]
        if (distance <= EPSILON) {
            // HACK: Estimate a likely intersection point
            intersectionDistance = positionAlongRay - abs(distance);
            return true;
        }

        float stepSize = max(MIN_STEP_SIZE, abs(distance));
        positionAlongRay += stepSize;
    }

    return false;
}

void ObjectSurfacesPixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart      : POSITION1,
    in  float3 rayVector     : POSITION2,
    in  float2 vpos          : VPOS,
    out float4 result        : COLOR0
) {
    float intersectionDistance;
    if (traceRay(rayStart, rayVector, intersectionDistance)) {
        result = float4(0, 0, 0, 0);
    } else {
        result = float4(1, 1, 1, 1);
    }
}

technique ObjectSurfaces {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader  = compile ps_3_0 ObjectSurfacesPixelShader();
    }
}