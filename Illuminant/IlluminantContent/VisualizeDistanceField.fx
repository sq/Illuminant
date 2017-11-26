#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants

#include "VisualizeCommon.fxh"

uniform float3 AmbientColor;
uniform float3 LightDirection;
uniform float3 LightColor;

uniform float Time;

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

void ObjectSurfacesPixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart  : TEXCOORD0,
    in  float3 rayVector : TEXCOORD1,
    in  float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float intersectionDistance;
    float3 estimatedIntersection;

    if (traceSurface(rayStart, rayVector, intersectionDistance, estimatedIntersection, vars)) {
        result = float4(AmbientColor, 1.0);

        float3 normal = estimateNormal(estimatedIntersection, vars);

        float normalDotLight = dot(normal, LightDirection);
        normalDotLight = clamp((normalDotLight + 0.05) * 1.1, 0, 1);
        result.rgb += LightColor * normalDotLight * color.rgb;
    }
    else {
        result = 0;
        discard;
    }
}

void ObjectOutlinesPixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart : TEXCOORD0,
    in  float3 rayVector : TEXCOORD1,
    in  float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float a = traceOutlines(rayStart, rayVector, vars);

    result = float4(a, a, a, a) * color;

    if (a <= 0)
        discard;
}

technique ObjectSurfaces {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader  = compile ps_3_0 ObjectSurfacesPixelShader();
    }
}

technique ObjectOutlines {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader = compile ps_3_0 ObjectOutlinesPixelShader();
    }
}