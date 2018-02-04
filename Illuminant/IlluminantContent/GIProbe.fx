#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define OFFSET 1
#define SEARCH_DISTANCE 1024

#include "VisualizeCommon.fxh"

#include "SphericalHarmonics.fxh"

static const int NormalCount = 8;
static const float3 Normals[] = {
    {-1, 0, 0}
    ,{1, 0, 0}
    ,{0, -1, 0}
    ,{0, 1, 0}
    ,{-1, -1, 0}
    ,{ 1, 1, 0 }
    ,{ 1, -1, 0 }
    ,{ -1, 1, 0 }
    // TODO: Add down vector and collide with ground plane (based on GBuffer maybe?)
};

uniform float Time;

// uniform float  MaxSearchDistance;
uniform float2 RequestedPositionTexelSize, ProbeValuesTexelSize;

Texture2D RequestedPositions       : register(t2);
sampler   RequestedPositionSampler : register(s2) {
    Texture = (RequestedPositions);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

Texture2D ProbeValues        : register(t4);
sampler   ProbeValuesSampler : register(s4) {
    Texture = (ProbeValues);
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

void ProbeSelectorPixelShader(
    in  float2 vpos           : VPOS,
    out float4 resultPosition : COLOR0,
    out float4 resultNormal   : COLOR1
) {
    float3 normal = normalize(Normals[vpos.y]);

    float2 uv = vpos * RequestedPositionTexelSize;
    float3 requestedPosition = tex2Dlod(RequestedPositionSampler, float4(uv, 0, 0)).xyz;

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float initialDistance = sampleDistanceField(requestedPosition, vars);

    [branch]
    if (initialDistance <= 2) {
        resultPosition = 0;
        resultNormal = 0;
        return;
    }

    float intersectionDistance;
    float3 estimatedIntersection;
    float3 ray = normal * SEARCH_DISTANCE;

    if (traceSurface(requestedPosition, ray, intersectionDistance, estimatedIntersection, vars)) {
        resultPosition = float4(estimatedIntersection - (normal * OFFSET), 1);
        resultNormal = float4(-normal, 1);
    } else {
        resultPosition = 0;
        resultNormal = 0;
    }
}

void SHGeneratorPixelShader(
    in  float2 vpos   : VPOS,
    out float4 result : COLOR0
) {
    float x = vpos.x * ProbeValuesTexelSize.x;

    float ra = 0, rb = 0, rc = 0, rd = 0, re = 0, rf = 0, rg = 0, rh = 0, ri = 0;

    for (int idx = 0; idx < NormalCount; idx++) {
        float4 uv = float4(x, idx * ProbeValuesTexelSize.y, 0, 0);
        // FIXME: InverseScaleFactor
        float3 value = tex2Dlod(ProbeValuesSampler, uv) * float3(0.299f, 0.587f, 0.114f);
        float valueGrey = value.r + value.g + value.b;

        float3 normal = normalize(Normals[idx]);
        float a, b, c, d, e, f, g, h, i;
        SHCosineLobe(normal, a, b, c, d, e, f, g, h, i);

        ra += a * valueGrey;
        rb += b * valueGrey;
        rc += c * valueGrey;
        rd += d * valueGrey;
        re += e * valueGrey;
        rf += f * valueGrey;
        rg += g * valueGrey;
        rh += h * valueGrey;
        ri += i * valueGrey;
    }

    if (vpos.y == 0)
        result = ra / NormalCount;
    else if (vpos.y == 1)
        result = rb / NormalCount;
    else if (vpos.y == 2)
        result = rc / NormalCount;
    else if (vpos.y == 3)
        result = rd / NormalCount;
    else if (vpos.y == 4)
        result = re / NormalCount;
    else if (vpos.y == 5)
        result = rf / NormalCount;
    else if (vpos.y == 6)
        result = rg / NormalCount;
    else if (vpos.y == 7)
        result = rh / NormalCount;
    else
        result = ri / NormalCount;
}

void SHVisualizerPixelShader(
    in float2  localPosition : TEXCOORD0,
    in int2    probeIndex    : BLENDINDICES0,
    out float4 result        : COLOR0
) {
    float sh[9];
    float irradiance = 0;

    for (int y = 0; y < SHTexelCount; y++) {
        float4 uv = float4(probeIndex.x * ProbeValuesTexelSize.x, y * ProbeValuesTexelSize.y, 0, 0);
        sh[y] = tex2Dlod(ProbeValuesSampler, uv).r;
    }

    float3 normal = normalize(float3(localPosition.x, localPosition.y, 0.001));
    float a, b, c, d, e, f, g, h, i;
    SHCosineLobe(normal, a, b, c, d, e, f, g, h, i);

    irradiance += a * sh[0];
    irradiance += b * sh[1];
    irradiance += c * sh[2];
    irradiance += d * sh[3];
    irradiance += e * sh[4];
    irradiance += f * sh[5];
    irradiance += g * sh[6];
    irradiance += h * sh[7];
    irradiance += i * sh[8];

    // FIXME: InverseScaleFactor
    float resultGrey = irradiance * (1.0f / Pi);
    result = float4(resultGrey, resultGrey, resultGrey, 1);
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