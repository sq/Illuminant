#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "Bezier.fxh"

uniform ClampedBezier4 Bezier;

void ScreenSpaceBezierVisualizerVertexShader (
    in float2 position : POSITION0, // x, y
    inout float4 color : COLOR0,
    inout float2 xy : TEXCOORD0,
    out float4 result : POSITION0
) {
    result = TransformPosition(float4(position.xy, 0, 1), 0);
}

void BezierVisualizerPixelShader (
    in float4 color   : COLOR0,
    in float2 xy      : TEXCOORD0,
    out float4 result : COLOR0
) {
    float4 value = evaluateBezier4(Bezier, xy.x);
    result = float4(value.r, value.g, value.b, 1);
    // result *= color;
}

technique ScreenSpaceBezierVisualizer
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceBezierVisualizerVertexShader();
        pixelShader = compile ps_3_0 BezierVisualizerPixelShader();
    }
}