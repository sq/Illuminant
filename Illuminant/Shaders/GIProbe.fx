#pragma fxcparams(/Od /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\DitherCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define OFFSET 1
#define FUDGE 0.375
#define DO_FIRST_BOUNCE true
#define DROP_DEAD_SAMPLES_FROM_SH false
// FIXME: Broken somehow
#define ESTIMATED_NORMALS false

// Lower than the vis shaders to make sure we get well-placed GI probe positions
#define TRACE_MIN_STEP_SIZE 0.5
#define TRACE_FINAL_MIN_STEP_SIZE 2

#include "VisualizeCommon.fxh"

#include "SphericalHarmonics.fxh"

uniform float NormalCount;
static const float NormalSliceCount = 3;
static const float SliceIndexToZ = 2.5;

uniform float BounceSearchDistance;
uniform float InverseScaleFactor;
uniform float Brightness;

uniform float2 ProbeValuesTexelSize, PreviousBounceTexelSize;

Texture2D ProbeValues        : register(t5);
sampler   ProbeValuesSampler : register(s5) {
    Texture = (ProbeValues);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

Texture2D PreviousBounce        : register(t6);
sampler   PreviousBounceSampler : register(s6) {
    Texture = (PreviousBounce);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void ProbeVertexShader (
    inout float4 position      : POSITION0,
    inout float4 probeOffsetAndBaseIndex : TEXCOORD0,
    inout float4 probeIntervalAndCount   : TEXCOORD1
) {
}

float3 ComputeRowNormal(float row) {
    if (row < 1)
        return float3(0, 0, 1);

    float sliceCount = max(0.001, NormalSliceCount);

    row -= 1;
    float normalSliceSize = max(0.001, floor((NormalCount - 1) / sliceCount));
    float sliceIndex = floor(row / normalSliceSize);
    float radians = (row + (sliceIndex * 0.33)) / normalSliceSize * 2 * Pi + (Dithering.FrameIndex * 0.001);

    float2 xy;
    sincos(radians, xy.x, xy.y);

    return normalize(float3(xy, sliceIndex / SliceIndexToZ));
}

// FIXME: This is broken in any optimization mode except /Od
void ProbeSelectorPixelShader(
    ACCEPTS_VPOS,
    in  float4 probeOffsetAndBaseIndex : TEXCOORD0,
    in  float4 probeIntervalAndCount   : TEXCOORD1,
    out float4 resultPosition          : COLOR0,
    out float4 resultNormal            : COLOR1
) {
    float2 probeInterval = probeIntervalAndCount.xy;
    float2 probeCount = probeIntervalAndCount.zw;
    float probeCountDivisor = max(0.001, probeCount.x);
    float rawIndex = GET_VPOS.x - probeOffsetAndBaseIndex.w;
    float yIndex = floor(rawIndex / probeCountDivisor);
    float xIndex = rawIndex - (yIndex * probeCount.x);
    float3 requestedPosition = probeOffsetAndBaseIndex.xyz + float3(probeInterval.x * xIndex, probeInterval.y * yIndex, 0);
    float3 normal = ComputeRowNormal(GET_VPOS.y);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float initialDistance = sampleDistanceField(requestedPosition, vars);

    resultPosition = 0;
    resultNormal = 0;

    [branch]
    if (initialDistance <= 1.66)
        return;

    if (DO_FIRST_BOUNCE) {
        float intersectionDistance;
        float3 estimatedIntersection;
        float3 ray = normal * BounceSearchDistance;

        if (traceSurface(requestedPosition, ray, intersectionDistance, estimatedIntersection, vars)) {
            resultPosition = float4(
                estimatedIntersection, 1
            );

            if (ESTIMATED_NORMALS) {
                float3 estimatedNormal = estimateNormal4(estimatedIntersection, vars);
                resultNormal = float4(estimatedNormal, 1);
            } else {
                resultNormal = float4(-normal, 1);
            }
        }
    } else {
        resultPosition = float4(requestedPosition, 1);
        resultNormal = float4(normal, 1);
    }
}

void SHGeneratorPixelShader(
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    float2 vpos = GET_VPOS;
    int y = max(0, floor(vpos.y));

    SH9Color r;

    float received = 0.001;
    float divisor = 0.001;

    for (float idx = 0; idx < NormalCount; idx++) {
        float4 uv = float4((vpos.x + FUDGE) * ProbeValuesTexelSize.x, (idx + FUDGE) * ProbeValuesTexelSize.y, 0, 0);
        float4 value = tex2Dlod(ProbeValuesSampler, uv);

        if (value.w < 0.9)
            continue;

        float3 normal = ComputeRowNormal(idx);
        SH9 cos = SHCosineLobe(normal);

        SH9CAdd9(r, cos, value.rgb * InverseScaleFactor);

        received += 1;
    }

    if (DROP_DEAD_SAMPLES_FROM_SH)
        SHScaleColorByCosine(r, received);
    else
        SHScaleColorByCosine(r, max(0.001, NormalCount));

    float3 previousBounceValue = 0;

    if (any(PreviousBounceTexelSize)) {
        float4 uv = float4((vpos + FUDGE) * PreviousBounceTexelSize, 0, 0);
        float4 oldSample = tex2Dlod(PreviousBounceSampler, uv);
        previousBounceValue = oldSample.rgb;
    }

    float3 row;
    switch (y) {
        case 0:
            row = r.a;
            break;
        case 1:
            row = r.b;
            break;
        case 2:
            row = r.c;
            break;
        case 3:
            row = r.d;
            break;
        case 4:
            row = r.e;
            break;
        case 5:
            row = r.f;
            break;
        case 6:
            row = r.g;
            break;
        case 7:
            row = r.h;
            break;
        case 8:
        default:
            row = r.i;
            break;
    }

    result.rgb = (row * Brightness) + previousBounceValue;
    result.a = received;
}

technique ProbeSelector {
    pass P0
    {
        vertexShader = compile vs_3_0 ProbeVertexShader();
        pixelShader = compile ps_3_0 ProbeSelectorPixelShader();
    }
}

technique SHGenerator {
    pass P0
    {
        vertexShader = compile vs_3_0 ProbeVertexShader();
        pixelShader = compile ps_3_0 SHGeneratorPixelShader();
    }
}