#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define OFFSET 1
#define FUDGE 0.375
#define DO_FIRST_BOUNCE true
#define DROP_DEAD_SAMPLES_FROM_SH true

#include "VisualizeCommon.fxh"

#include "SphericalHarmonics.fxh"

uniform float NormalCount;
static const float NormalSliceCount = 3;
static const float SliceIndexToZ = 2.5;

uniform float Time;

uniform float BounceFalloffDistance, BounceSearchDistance;

uniform float2 RequestedPositionTexelSize, ProbeValuesTexelSize, SphericalHarmonicsTexelSize;

Texture2D RequestedPositions       : register(t2);
sampler   RequestedPositionSampler : register(s2) {
    Texture = (RequestedPositions);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

Texture2D ProbeValues        : register(t5);
sampler   ProbeValuesSampler : register(s5) {
    Texture = (ProbeValues);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

Texture2D SphericalHarmonics        : register(t6);
sampler   SphericalHarmonicsSampler : register(s6) {
    Texture = (SphericalHarmonics);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void ProbeVertexShader (
    inout float4 position      : POSITION0
) {
}

void SHVisualizerVertexShader (
    in    float4 position       : POSITION0,
    inout float2 localPosition  : TEXCOORD0,
    inout int2   probeIndex     : BLENDINDICES0,
    out   float4 result         : POSITION0
) {
    result = TransformPosition(float4(position.xy, 0, 1), 0);
}

float3 ComputeRowNormal(float row) {
    if (row < 1)
        return float3(0, 0, 1);

    row -= 1;
    float normalSliceSize = floor((NormalCount - 1) / NormalSliceCount);
    float sliceIndex = floor(row / normalSliceSize);
    float radians = (row + (sliceIndex * 0.33)) / normalSliceSize * 2 * Pi;

    float2 xy;
    sincos(radians, xy.x, xy.y);

    return normalize(float3(xy, sliceIndex / SliceIndexToZ));
}

void ProbeSelectorPixelShader(
    in  float2 vpos           : VPOS,
    out float4 resultPosition : COLOR0,
    out float4 resultNormal   : COLOR1
) {
    float2 uv = (vpos + FUDGE) * RequestedPositionTexelSize;
    float3 requestedPosition = tex2Dlod(RequestedPositionSampler, float4(uv, 0, 0)).xyz;
    int y = max(0, floor(vpos.y));

    float3 normal = ComputeRowNormal(y);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float initialDistance = sampleDistanceField(requestedPosition, vars);

    [branch]
    if (initialDistance <= 2) {
        resultPosition = 0;
        resultNormal = 0;
        return;
    }

    if (DO_FIRST_BOUNCE) {
        float intersectionDistance;
        float3 estimatedIntersection;
        float3 ray = normal * BounceSearchDistance;

        if (traceSurface(requestedPosition, ray, intersectionDistance, estimatedIntersection, vars)) {
            resultPosition = float4(
                estimatedIntersection - (normal * OFFSET), 
                1 - clamp(intersectionDistance / BounceFalloffDistance, 0, 1)
            );
            resultNormal = float4(-normal, 1);
        } else {
            resultPosition = 0;
            resultNormal = 0;
        }
    } else {
        resultPosition = float4(requestedPosition, 1);
        resultNormal = float4(normal, 1);
    }
}

void SHGeneratorPixelShader(
    in  float2 vpos   : VPOS,
    out float4 result : COLOR0
) {
    int y = max(0, floor(vpos.y));

    SH9Color r;

    float received = 0;
    float divisor = 0.001;

    for (float idx = 0; idx < NormalCount; idx++) {
        float4 uv = float4((vpos.x + FUDGE) * ProbeValuesTexelSize.x, (idx + FUDGE) * ProbeValuesTexelSize.y, 0, 0);
        // FIXME: InverseScaleFactor
        float4 value = tex2Dlod(ProbeValuesSampler, uv);

        if (value.w < 0.9)
            continue;

        float3 normal = ComputeRowNormal(idx);
        SH9 cos = SHCosineLobe(normal);

        for (int j = 0; j < SHValueCount; j++)
            r.c[j] += cos.c[j] * value.rgb;

        received += 1;
    }

    if (DROP_DEAD_SAMPLES_FROM_SH)
        SHScaleColorByCosine(r, received);
    else
        SHScaleColorByCosine(r, NormalCount);

    result.rgb = r.c[y];
    result.a = received;
}

void SHVisualizerPixelShader(
    in float2  localPosition : TEXCOORD0,
    in int2    _probeIndex   : BLENDINDICES0,
    out float4 result        : COLOR0
) {
    SH9Color rad;
    float3 irradiance = 0;

    float probeIndex = _probeIndex.x;
    float4 uv = float4((probeIndex + FUDGE) * SphericalHarmonicsTexelSize.x, 0, 0, 0);
    float received = 0;

    for (int y = 0; y < SHTexelCount; y++) {
        uv.y = (y + FUDGE) * SphericalHarmonicsTexelSize.y;
        float4 coeff = tex2Dlod(SphericalHarmonicsSampler, uv);
        rad.c[y] = coeff.rgb;
        received += coeff.a;
    }

    [branch]
    if (received < 1) {
        discard;
        return;
    }

    float xyLength = length(localPosition);
    float z = 1 - clamp(xyLength / 0.9, 0, 1);
    // FIXME: Correct?
    float3 normal = float3(localPosition.x * -1.1, localPosition.y * -1.1, z * z);
    normal = normalize(normal);
    SH9 cos = SHCosineLobe(normal);

    SHScaleByCosine(cos);

    // FIXME: This doesn't seem like it should be necessary but without it the probes look really dark
    SHScaleColorByCosine(rad, 1);

    for (int i = 0; i < SHValueCount; i++)
        irradiance += rad.c[i] * cos.c[i];

    // FIXME: InverseScaleFactor
    float3 resultRgb = (irradiance * irradiance)
        // HACK: This seems to be needed to compensate for the cosine scaling of the color value
        * (1.0 / Pi);
    float  fade = 1.0 - clamp((xyLength - 0.9) / 0.1, 0, 1);
    result = float4(resultRgb * fade, fade);
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

technique SHVisualizer {
    pass P0
    {
        vertexShader = compile vs_3_0 SHVisualizerVertexShader();
        pixelShader = compile ps_3_0 SHVisualizerPixelShader();
    }
}