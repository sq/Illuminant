#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "Bezier.fxh"

uniform ClampedBezier4 Bezier;
uniform float CurrentT;

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
    in float2 vpos    : VPOS,
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

    /*
    float4 valueRanges = maxValues - minValues;
    valueRanges = max(abs(valueRanges), 0.001) * sign(valueRanges);
    */

    float minValue = min(minValues.r, min(minValues.g, min(minValues.b, minValues.a)));
    float maxValue = max(maxValues.r, max(maxValues.g, max(maxValues.b, maxValues.a)));
    float valueRange = maxValue - minValue;
    valueRange = max(abs(valueRange), 0.001) * sign(valueRange);

    float scaledT;
    if (CurrentT >= -999)
        tForScaledBezier(Bezier.RangeAndCount, CurrentT, scaledT);
    else
        scaledT = CurrentT;

    float4 value = evaluateBezier4AtT(Bezier, count, xy.x);
    float4 scaledValue = saturate((value - minValue) / valueRange);
    float4 distances = abs((1 - xy.y) - scaledValue);
    float4 scaledDistances = 1 - saturate(distances / 0.016);

    float elementCount = abs(Bezier.RangeAndCount.w);
    if (elementCount < 1.5)
        scaledDistances.yzw = 0;
    else if (elementCount < 2.5)
        scaledDistances.zw = 0;
    else if (elementCount < 3.5)
        scaledDistances.w = 0;

    float2 scaledOneAndZero = saturate((float2(1, 0) - minValue) / valueRange);
    float distanceAboveOne = saturate(((1 - xy.y) - scaledOneAndZero.x) / 0.015);
    float distanceBelowZero = saturate((scaledOneAndZero.y - (1 - xy.y)) / 0.015);
    float invDistanceToT = (1 - saturate(abs(xy.x - scaledT) / 0.01)) * 0.4;
    invDistanceToT = pow(invDistanceToT, 1.5);

    scaledDistances = pow(scaledDistances, 1.8);

    float4 w = (scaledDistances.a + invDistanceToT) * float4(1, 1, 1, 0);
    float  alpha = (scaledDistances.r + scaledDistances.g + scaledDistances.b + scaledDistances.a) + invDistanceToT;
    float  stipple = (vpos.x % 2) - (vpos.y % 2);
    alpha += pow(distanceAboveOne + distanceBelowZero, 1.25) * ((stipple * 0.08) + 0.4);
    result = float4(scaledDistances.rgb + w, saturate(alpha));
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