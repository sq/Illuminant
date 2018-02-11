#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"

#define OFFSET 1
#define FUDGE 0.375
#define SELF_OCCLUSION_HACK 1.5

#define ProbeLightCastsShadows true

#define ConeShadowRadius 4
#define ConeShadowRamp 2

#include "SphericalHarmonics.fxh"

uniform float Time;

uniform float Brightness;

uniform float2 SphericalHarmonicsTexelSize;

Texture2D SphericalHarmonics        : register(t6);
sampler   SphericalHarmonicsSampler : register(s6) {
    Texture = (SphericalHarmonics);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void PassthroughVertexShader (
    inout float4 position      : POSITION0,
    inout float4 probeOffsetAndBaseIndex : TEXCOORD0,
    inout float4 probeIntervalAndCount   : TEXCOORD1
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

float readSHProbe (
    float probeIndex, out SH9Color result
) {
    float4 uv = float4((probeIndex + FUDGE) * SphericalHarmonicsTexelSize.x, 0, 0, 0);
    float received = 0;

    for (int y = 0; y < SHTexelCount; y++) {
        uv.y = (y + FUDGE) * SphericalHarmonicsTexelSize.y;
        float4 coeff = tex2Dlod(SphericalHarmonicsSampler, uv);
        // FIXME: This loop is being unrolled here... is that bad?
        result.c[y] = coeff.rgb;
        received += coeff.a;
    }

    return received;
}

float readSHProbeXy(
    float2 indexXy,
    float4 probeOffsetAndBaseIndex,
    float4 probeIntervalAndCount,
    out SH9Color result,
    out float3 position
) {
    float2 probeInterval = probeIntervalAndCount.xy;
    float2 probeCount = probeIntervalAndCount.zw;

    float probeIndex = floor(indexXy.x + (indexXy.y * probeCount.x) + probeOffsetAndBaseIndex.w);
    position = probeOffsetAndBaseIndex.xyz + float3(probeInterval * indexXy, 0);
    return readSHProbe(probeIndex, result);
}

float3 computeSHIrradiance (
    in SH9Color probe, in float3 normal
) {
    SH9 cos = SHCosineLobe(normal);
    SHScaleByCosine(cos);

    float3 irradiance = 0;
    for (int i = 0; i < SHValueCount; i++)
        irradiance += probe.c[i] * cos.c[i];

    return irradiance;
}

void SHVisualizerPixelShader(
    in float2  localPosition : TEXCOORD0,
    in int2    _probeIndex   : BLENDINDICES0,
    out float4 result        : COLOR0
) {
    float probeIndex = _probeIndex.x;

    SH9Color rad;
    float received = readSHProbe(probeIndex, rad);

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

    float3 irradiance = computeSHIrradiance(rad, normal);

    float3 resultRgb = irradiance * Brightness;
        // HACK: This seems to be needed to compensate for the cosine scaling of the color value
        // * (1.0 / Pi);

    float  fade = 1.0 - clamp((xyLength - 0.9) / 0.1, 0, 1);
    result = float4(resultRgb * fade, fade);
}

float4 computeProbeRadiance(
    in float3 shadedPixelPosition,
    in float2 probeIndexXy,
    in float4 probeOffsetAndBaseIndex,
    in float4 probeIntervalAndCount,
    in SH9 cos,
    in DistanceFieldConstants vars
) {
    SH9Color probe;
    float3 probePosition;

    float received = readSHProbeXy(probeIndexXy, probeOffsetAndBaseIndex, probeIntervalAndCount, probe, probePosition);
    [branch]
    if (received < 1)
        return float4(0, 0, 0, 0);

    float3 vectorToProbe = probePosition - shadedPixelPosition;
    float3 normalToProbe = normalize(vectorToProbe);
    float3 localRadiance = 0;

    for (int j = 0; j < SHValueCount; j++)
        localRadiance += probe.c[j] * cos.c[j];

    float coneWeight = 1;

    if (ProbeLightCastsShadows)
        coneWeight = coneTrace(
            probePosition, float2(ConeShadowRadius, ConeShadowRamp),
            float2(getConeGrowthFactor(), 1),
            shadedPixelPosition + (SELF_OCCLUSION_HACK * normalToProbe),
            vars
        );

    float intervalFalloff = max(probeIntervalAndCount.x, probeIntervalAndCount.y);
    float intervalOffset = intervalFalloff + 2;
    float distanceWeight = 1 - clamp(
        max(0, length(vectorToProbe.xy) - intervalOffset) / intervalFalloff,
        0, 1
    );

    return float4(localRadiance * coneWeight, distanceWeight);
}

float4 conditionalBlend (float4 lhs, float4 rhs, float weight) {
    if (lhs.w < 1)
        return rhs;
    else if (rhs.w < 1)
        return lhs;

    return lerp(lhs, rhs, weight);
}

float3 SHRendererPixelShaderCore(
    float3 shadedPixelPosition,
    float3 shadedPixelNormal,
    float4 probeOffsetAndBaseIndex,
    float4 probeIntervalAndCount
) {
    float2 probeInterval = probeIntervalAndCount.xy;
    float2 probeCount = probeIntervalAndCount.zw;

    float2 minIndex = float2(0, 0);
    float2 maxIndex = probeCount - 1;

    float2 probeSpacePosition = shadedPixelPosition.xy - probeOffsetAndBaseIndex.xy;
    float2 probeIndexTl = clamp(floor(probeSpacePosition / probeInterval.xy), minIndex, maxIndex);
    float2 tlProbePosition = (probeIndexTl * probeInterval.xy);
    float2 probeIndexBr = clamp(ceil(probeSpacePosition / probeInterval.xy), minIndex, maxIndex);

    float2 weightXY = (probeSpacePosition - tlProbePosition) / probeInterval.xy;
    float2 probeIndices[4] = {
        probeIndexTl,
        float2(probeIndexBr.x, probeIndexTl.y),
        float2(probeIndexTl.x, probeIndexBr.y),
        probeIndexBr
    };
    float weights[4] = {
        0,
        weightXY.x * (1 - weightXY.y),
        (1 - weightXY.x) * weightXY.y,
        weightXY.x * weightXY.y,
    };
    weights[0] = 1 - (weights[1] + weights[2] + weights[3]);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    SH9 cos = SHCosineLobe(shadedPixelNormal);
    SHScaleByCosine(cos);

    float3 irradiance = 0;
    float maxDistanceWeight = 0;

    [loop]
    for (int i = 0; i < 4; i++) {
        float4 localRadiance = computeProbeRadiance(shadedPixelPosition, probeIndices[i], probeOffsetAndBaseIndex, probeIntervalAndCount, cos, vars);
        irradiance += localRadiance.rgb * weights[i];
        maxDistanceWeight = max(maxDistanceWeight, localRadiance.a);
    }

    return irradiance * maxDistanceWeight;
}

void SHRendererPixelShader(
    in  float2 vpos : VPOS,
    in  float4 probeOffsetAndBaseIndex : TEXCOORD0,
    in  float4 probeIntervalAndCount   : TEXCOORD1,
    out float4 result : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float3 irradiance = SHRendererPixelShaderCore(
        shadedPixelPosition, shadedPixelNormal, probeOffsetAndBaseIndex, probeIntervalAndCount
    ) * Brightness;

    result = float4(irradiance, 1);
}

void LightProbeSHRendererPixelShader(
    in  float2 vpos : VPOS,
    in  float4 probeOffsetAndBaseIndex : TEXCOORD0,
    in  float4 probeIntervalAndCount   : TEXCOORD1,
    out float4 result : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos + float2(probeOffsetAndBaseIndex.w, 0),
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    float3 irradiance = SHRendererPixelShaderCore(
        shadedPixelPosition, shadedPixelNormal, probeOffsetAndBaseIndex, probeIntervalAndCount
    ) * opacity * Brightness;

    result = float4(irradiance, 1);
}

technique VisualizeGI {
    pass P0
    {
        vertexShader = compile vs_3_0 SHVisualizerVertexShader();
        pixelShader = compile ps_3_0 SHVisualizerPixelShader();
    }
}

technique RenderGI {
    pass P0
    {
        vertexShader = compile vs_3_0 PassthroughVertexShader();
        pixelShader = compile ps_3_0 SHRendererPixelShader();
    }
}

technique RenderLightProbesFromGI {
    pass P0
    {
        vertexShader = compile vs_3_0 PassthroughVertexShader();
        pixelShader = compile ps_3_0 LightProbeSHRendererPixelShader();
    }
}