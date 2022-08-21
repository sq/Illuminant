#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "Bezier.fxh"

uniform ClampedBezier4 Bezier;
uniform const int   ElementCount;
uniform const float CurrentT;

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
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    float localT = (xy.x * 1.05) - 0.025;

    float count = Bezier.RangeAndCount.z;
    float4 minValues, maxValues;
    if (count <= 1.5) {
        minValues = min(0, Bezier.A);
        maxValues = max(1, Bezier.A);
    } else {
        minValues = min(Bezier.A, Bezier.B);
        maxValues = max(Bezier.A, Bezier.B);
        if (count > 2.1) {
            minValues = min(minValues, Bezier.C);
            maxValues = max(maxValues, Bezier.C);
        }
        if (count > 3.1) {
            minValues = min(minValues, Bezier.D);
            maxValues = max(maxValues, Bezier.D);
        }
    }

    /*
    float4 valueRanges = maxValues - minValues;
    valueRanges = max(abs(valueRanges), 0.001) * sign(valueRanges);
    */

    float4 valueRange = maxValues - minValues;

    float scaledT;
    if (CurrentT >= -999)
        tForScaledBezier(Bezier.RangeAndCount, CurrentT, scaledT);
    else
        scaledT = CurrentT;

    float4 value = evaluateBezier4AtT(Bezier, count, localT);
    float4 scaledValue = saturate4((value - minValues) / valueRange);
    float4 distances = abs((1 - xy.y) - scaledValue);
    float4 scaledDistances = 1 - saturate4(distances / 0.016);

    float elementCount = abs(ElementCount);
    if (elementCount < 1.5)
        scaledDistances.yzw = 0;
    else if (elementCount < 2.5)
        scaledDistances.zw = 0;
    else if (elementCount < 3.5)
        scaledDistances.w = 0;

    float2 scaledOneAndZero = saturate2((float2(1, 0) - minValues) / valueRange);
    float distanceAboveOne = saturate(((1 - xy.y) - scaledOneAndZero.x) / 0.015);
    float distanceBelowZero = saturate((scaledOneAndZero.y - (1 - xy.y)) / 0.015);
    float outside = saturate(max(saturate(-localT) * 100, saturate(localT - 1) * 100));
    float invDistanceToT = (1 - saturate(abs(localT - scaledT) / 0.01)) * 0.5;
    invDistanceToT = pow(invDistanceToT, 1.5);

    scaledDistances = pow(scaledDistances, 1.8);

    float4 w = (scaledDistances.a + invDistanceToT + (outside * 0.2)) * float4(1, 1, 1, 0);
    float  alpha = (scaledDistances.r + scaledDistances.g + scaledDistances.b + scaledDistances.a) + invDistanceToT;
    float2 vpos = GET_VPOS;
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