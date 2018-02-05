#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define OFFSET 1
#define FUDGE 0.375

#include "VisualizeCommon.fxh"

#include "SphericalHarmonics.fxh"

uniform float3 ProbeOffset;
uniform float2 ProbeInterval;
uniform float2 ProbeCount;

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

    result = float4((shadedPixelNormal * 0.5) + 0.5, 1);
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