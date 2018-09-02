#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

float evaluateFunctions (float3 worldPosition, float vars);

#define SAMPLE evaluateFunctions
#define TVARS  float
#define TRACE_MIN_STEP_SIZE 2
#define TRACE_FINAL_MIN_STEP_SIZE 12

#include "VisualizeCommon.fxh"

uniform float3 AmbientColor;
uniform float3 LightDirection;
uniform float3 LightColor;

uniform int    FunctionType;
uniform float3 FunctionCenter;
uniform float3 FunctionSize;

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(Viewport.Position.xy, 0)) * float3(Viewport.Scale, 1));
    return mul(mul(float4(localPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
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

void FunctionSlicePixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart : TEXCOORD0,
    in  float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float vars = 0;
    float distance = -(SAMPLE(rayStart, vars) / 64) + 0.5;
    distance = ApplyDither(distance, vpos).r;
    result = float4(distance, distance, distance, 1) * color;
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

technique FunctionSlice {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader = compile ps_3_0 FunctionSlicePixelShader();
    }
}