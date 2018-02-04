#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define OFFSET 1
#define SEARCH_DISTANCE 1024
#define FUDGE 0.375

#include "VisualizeCommon.fxh"

#include "SphericalHarmonics.fxh"

static const int NormalCount = 13;
static const float3 Normals[] = {
     { 0, 0, 1 }
    ,{-1, -1, 0}
    ,{ 1, 1, 0 }
    ,{ 1, -1, 0 }
    ,{ -1, 1, 0 }
    ,{ -1, 0, 1 }
    ,{ 1, 0, 1 }
    ,{ 0, -1, 1 }
    ,{ 0, 1, 1 }
    ,{ -1, 0, 0 }
    ,{ 1, 0, 0 }
    ,{ 0, -1, 0 }
    ,{ 0, 1, 0 }
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
    float2 uv = (vpos + FUDGE) * RequestedPositionTexelSize;
    float3 requestedPosition = tex2Dlod(RequestedPositionSampler, float4(uv, 0, 0)).xyz;
    int y = max(0, floor(vpos.y));

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float initialDistance = sampleDistanceField(requestedPosition, vars);

    [branch]
    if (initialDistance <= 2) {
        resultPosition = 0;
        resultNormal = 0;
        return;
    }

    resultPosition = float4(requestedPosition.xyz, 1);
    resultNormal = float4(normalize(Normals[y]), 1);

    /*

    [branch]
    if (vpos.y < 0.9) {
        resultPosition = float4(requestedPosition.xyz, 1);
        resultNormal = float4(0, 0, 1, 1);
        return;
    } else {
        float3 normal = normalize(Normals[vpos.y]);

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

    */
}

void SHGeneratorPixelShader(
    in  float2 vpos   : VPOS,
    out float4 result : COLOR0
) {
    int y = max(0, floor(vpos.y));

    SH9Color r;
    for (int i = 0; i < SHValueCount; i++)
        r.c[i] = 0;

    float divisor = 0;

    for (int idx = 0; idx < NormalCount; idx++) {
        float4 uv = float4((vpos.x + FUDGE) * ProbeValuesTexelSize.x, (idx + FUDGE) * ProbeValuesTexelSize.y, 0, 0);
        // FIXME: InverseScaleFactor
        float4 value = tex2Dlod(ProbeValuesSampler, uv);

        if (value.w < 0.9)
            continue;

        float3 normal = normalize(Normals[idx]);
        SH9 cos = SHCosineLobe(normal);

        for (int j = 0; j < SHValueCount; j++)
            r.c[j] += cos.c[j] * value.rgb;

        divisor += 1;
    }

    result.rgb = r.c[y] / divisor;
    result.a = 1;
}

void SHVisualizerPixelShader(
    in float2  localPosition : TEXCOORD0,
    in int2    probeIndex    : BLENDINDICES0,
    out float4 result        : COLOR0
) {
    SH9Color rad;
    float3 irradiance = 0;

    for (int y = 0; y < SHTexelCount; y++) {
        float4 uv = float4((probeIndex.x + FUDGE) * ProbeValuesTexelSize.x, (y + FUDGE) * ProbeValuesTexelSize.y, 0, 0);
        rad.c[y] = tex2Dlod(ProbeValuesSampler, uv).rgb;
    }

    float xyLength = length(localPosition);
    float z = 1 - clamp(xyLength / 0.9, 0, 1);
    float3 normal = float3(localPosition.x * 1.1, localPosition.y * 1.1, z * z);
    normal = normalize(normal);
    SH9 cos = SHCosineLobe(normal);

    for (int i = 0; i < SHValueCount; i++)
        irradiance += rad.c[i] * cos.c[i];

    // FIXME: InverseScaleFactor
    float3 resultRgb = irradiance * irradiance;
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