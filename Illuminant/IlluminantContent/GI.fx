#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"

#define OFFSET 1
#define FUDGE 0.375
#define SELF_OCCLUSION_HACK 1.5

#define ProbeLightDistanceFalloff false
#define ProbeLightCastsShadows true
#define ProbeLightUsesPerProbeNormals false

#define ConeShadowRadius 4
#define ConeShadowRamp 2

#include "SphericalHarmonics.fxh"

uniform float3 ProbeOffset;
uniform float2 ProbeInterval;
uniform float2 ProbeCount;

uniform float Time;

uniform float Brightness;
uniform float RadianceFalloffDistance;

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

float readSHProbe (
    float probeIndex, out SH9Color result
) {
    float4 uv = float4((probeIndex + FUDGE) * SphericalHarmonicsTexelSize.x, 0, 0, 0);
    float received = 0;

    for (int y = 0; y < SHTexelCount; y++) {
        uv.y = (y + FUDGE) * SphericalHarmonicsTexelSize.y;
        float4 coeff = tex2Dlod(SphericalHarmonicsSampler, uv);
        result.c[y] = coeff.rgb;
        received += coeff.a;
    }

    return received;
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

    // FIXME: InverseScaleFactor
    float3 resultRgb = irradiance * Brightness;
        // HACK: This seems to be needed to compensate for the cosine scaling of the color value
        // * (1.0 / Pi);

    float  fade = 1.0 - clamp((xyLength - 0.9) / 0.1, 0, 1);
    result = float4(resultRgb * fade, fade);
}

float readSHProbeXy (float2 indexXy, out SH9Color result, out float3 position) {
    float probeIndex = floor(indexXy.x + (indexXy.y * ProbeCount.x));
    position = ProbeOffset + float3(ProbeInterval * indexXy, 0);
    return readSHProbe(probeIndex, result);
}

void SHRendererPixelShader(
    in float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float2 minIndex = float2(0, 0);
    float2 maxIndex = ProbeCount - 1;

    float2 probeSpacePosition = shadedPixelPosition.xy - ProbeOffset;
    float2 probeIndexTl = clamp(floor(probeSpacePosition / ProbeInterval.xy), minIndex, maxIndex);
    float2 tlProbePosition = (probeIndexTl * ProbeInterval.xy);
    float2 probeIndexBr = clamp(ceil(probeSpacePosition / ProbeInterval.xy), minIndex, maxIndex);

    float2 probeIndices[4] = { 
        probeIndexTl, 
        float2(probeIndexBr.x, probeIndexTl.y), 
        float2(probeIndexTl.x, probeIndexBr.y),
        probeIndexBr
    };
    float2 weightXY = (probeSpacePosition - tlProbePosition) / ProbeInterval.xy;
    float weights[4] = {
        0,
        weightXY.x * (1 - weightXY.y),
        (1 - weightXY.x) * weightXY.y,
        weightXY.x * weightXY.y,
    };
    weights[0] = 1 - (weights[1] + weights[2] + weights[3]);

    /*
    result = float4(weights[0], weights[1], weights[2], 1);
    return;
    */

    /*
    result = float4(probeIndexXy.x / 40, probeIndexXy.y / 40, probeIndex / 1024, 1);
    return;
    */

    float3 irradiance = 0;
    float divisor = 0.0001;

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    SH9 cos;

    if (!ProbeLightUsesPerProbeNormals) {
        cos = SHCosineLobe(shadedPixelNormal);
        SHScaleByCosine(cos);
    }

    [loop]
    for (int i = 0; i < 4; i++) {
        SH9Color probe;
        float3 probePosition;

        float received = readSHProbeXy(probeIndices[i], probe, probePosition);
        [branch]
        if (received < 1)
            continue;

        float3 vectorToProbe = probePosition - shadedPixelPosition;
        float3 normalToProbe = normalize(vectorToProbe);

        if (ProbeLightUsesPerProbeNormals) {
            cos = SHCosineLobe(normalToProbe);
            SHScaleByCosine(cos);
        }

        float3 localIrradiance = 0;
        for (int j = 0; j < SHValueCount; j++)
            localIrradiance += probe.c[j] * cos.c[j];

        // float3 normal = normalize(vectorToProbe);
        float distanceWeight = 1, coneWeight = 1;

        if (ProbeLightDistanceFalloff) {
            float distance = length(vectorToProbe);
            distanceWeight -= clamp(distance / RadianceFalloffDistance, 0, 1);
        }

        if (ProbeLightCastsShadows)
            coneWeight = coneTrace(
                probePosition, float2(ConeShadowRadius, ConeShadowRamp),
                float2(getConeGrowthFactor(), 1),
                shadedPixelPosition + (SELF_OCCLUSION_HACK * normalToProbe),
                vars
            );

        float localWeight = distanceWeight * weights[i] * coneWeight;

        irradiance += (localIrradiance * localWeight);
    }

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