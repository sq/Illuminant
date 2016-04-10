#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

float evaluateFunctions (float3 worldPosition, float vars);

#define SAMPLE evaluateFunctions
#define TVARS  float

#include "VisualizeCommon.fxh"

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float Time;

uniform int    FunctionType;
uniform float3 FunctionCenter;
uniform float3 FunctionSize;

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(ViewportPosition.xy, 0)) * float3(ViewportScale, 1));
    return mul(mul(float4(localPosition.xyz, 1), ModelViewMatrix), ProjectionMatrix);
}

void VisualizeVertexShader(
    in float3 position       : POSITION0,
    inout float3 rayStart    : TEXCOORD0,
    inout float3 rayVector   : TEXCOORD1,
    inout float4 color       : COLOR0,
    out float3 worldPosition : TEXCOORD2,
    out float4 result        : POSITION0
) {
    worldPosition = position;
    float4 transformedPosition = ApplyTransform(position);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

[call]
float evaluateFunctions (float3 worldPosition, float vars) {
    if (FunctionType < 1) {
        return evaluateEllipsoid(worldPosition, FunctionCenter, FunctionSize);
    } else if (FunctionType < 2) {
        return evaluateBox(worldPosition, FunctionCenter, FunctionSize);
    } else {
        return evaluateCylinder(worldPosition, FunctionCenter, FunctionSize);
    }
}

void FunctionSurfacePixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart : TEXCOORD0,
    in  float3 rayVector : TEXCOORD1,
    in  float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float vars = 0;

    float intersectionDistance;
    float3 estimatedIntersection;
    if (traceSurface(rayStart, rayVector, intersectionDistance, estimatedIntersection, vars)) {
        result = float4(AmbientColor, 1.0);

        float3 normal = estimateNormal(estimatedIntersection, vars);
        float normalDotLight = dot(normal, LightDirection);
        if (normalDotLight > 0)
            result.rgb += LightColor * normalDotLight * color.rgb;
    }
    else {
        result = 0;
        discard;
    }
}

void FunctionOutlinePixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart : TEXCOORD0,
    in  float3 rayVector : TEXCOORD1,
    in  float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float vars = 0;

    float a = traceOutlines(rayStart, rayVector, vars);

    result = float4(a, a, a, a) * color;

    if (a <= 0)
        discard;
}

technique FunctionSurface {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader  = compile ps_3_0 FunctionSurfacePixelShader();
    }
}

technique FunctionOutline {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader = compile ps_3_0 FunctionOutlinePixelShader();
    }
}