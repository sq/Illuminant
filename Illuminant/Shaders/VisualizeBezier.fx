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
    float count = Bezier.RangeAndCount.z;
    float4 minValues, maxValues;
    if (count <= 1.5) {
        minValues = 0;
        maxValues = 1;
    } else {
        minValues = min(Bezier.A, min(Bezier.B, min(Bezier.C, Bezier.D)));
        maxValues = max(Bezier.A, max(Bezier.B, max(Bezier.C, Bezier.D)));
    }
    float4 valueRanges = maxValues - minValues;
    valueRanges = max(abs(valueRanges), 0.001) * sign(valueRanges);
    float4 value = evaluateBezier4AtT(Bezier, count, xy.x);
    float4 scaledValue = saturate((value - minValues) / valueRanges);
    float4 distances = abs(xy.y - scaledValue);
    float4 scaledDistances = 1 - saturate(distances / 0.0275);
    float4 w = scaledDistances.a * float4(1, 1, 1, 0);

    result = float4(scaledDistances.rgb + w, 1);
    result *= color;
}

technique ScreenSpaceBezierVisualizer
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceBezierVisualizerVertexShader();
        pixelShader = compile ps_3_0 BezierVisualizerPixelShader();
    }
}