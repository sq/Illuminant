// FIXME: In some backends the function visualization just completely breaks to bits in O3
#pragma fxcparams(/Od /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "DistanceFunctionCommon.fxh"

float evaluateFunctions (float3 worldPosition, float vars);

#define SAMPLE evaluateFunctions
#define TVARS  float
#define TRACE_MIN_STEP_SIZE 2
#define TRACE_FINAL_MIN_STEP_SIZE 12

#define OUTLINE_SIZE OutlineSize
#define FILL_INTERIOR FilledInterior
#define VISUALIZE_TEXEL float4(0.75, 0.75, 0.75, 0)

uniform const float  FilledInterior;
uniform const float  OutlineSize;

uniform const float3 AmbientColor;
uniform const float3 LightDirection;
uniform const float3 LightColor;

uniform const int    FunctionType;
uniform const float3 FunctionCenter;
uniform const float3 FunctionSize;
uniform const float  FunctionRotation;

#include "VisualizeCommon.fxh"

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(GetViewportPosition(), 0)) * float3(GetViewportScale(), 1));
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
    return evaluateByTypeId(FunctionType, worldPosition, FunctionCenter, FunctionSize, FunctionRotation);
}

void FunctionSurfacePixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart : TEXCOORD0,
    in  float3 rayVector : TEXCOORD1,
    in  float4 color : COLOR0,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    float vars = 0;

    float intersectionDistance;
    float3 estimatedIntersection;
    if (traceSurface(rayStart, rayVector, intersectionDistance, estimatedIntersection, vars)) {
        result = float4(AmbientColor, 1.0);

        float3 normal = estimateNormal4(estimatedIntersection, vars);
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
    ACCEPTS_VPOS,
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