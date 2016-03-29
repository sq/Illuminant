#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SELF_OCCLUSION_HACK 1.1

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float Time;

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(ViewportPosition.xy, 0)) * float3(ViewportScale, 1));
    return mul(mul(float4(localPosition.xyz, 1), ModelViewMatrix), ProjectionMatrix);
}

void PointLightVertexShader(
    in float2 position : POSITION0,
    inout float4 color : COLOR0,
    inout float3 lightCenter : TEXCOORD0,
    inout float3 rampAndExponential : TEXCOORD1,
    out float2 worldPosition : TEXCOORD2,
    out float4 result : POSITION0
) {
    worldPosition = position;
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, lightCenter.z));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float PointLightPixelCore(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter   : TEXCOORD0,
    in float2 ramp          : TEXCOORD1, // radius, ramp length
    in float2 vpos          : VPOS,
    float     exponential
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    shadedPixelPosition.z += SELF_OCCLUSION_HACK;

    float lightOpacity = computeLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, ramp.x, ramp.y
    );

    if (exponential)
        lightOpacity *= lightOpacity;

    [branch]
    if (lightOpacity >= (1.0 / 255.0)) {
        float tracedOcclusion = coneTrace(lightCenter, ramp, shadedPixelPosition);
        return lightOpacity * tracedOcclusion;
    } else {
        discard;
        return 0;
    }
}

void PointLightPixelShader(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float3 rampAndExponential : TEXCOORD1, // start, end, exp
    in float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float distanceOpacity = PointLightPixelCore(
        worldPosition, lightCenter, rampAndExponential.xy, vpos, rampAndExponential.z
    );

    float4 lightColorActual = float4(color.rgb * color.a * distanceOpacity, color.a * distanceOpacity);
    result = lightColorActual;
}

technique PointLight {
    pass P0
    {
        vertexShader = compile vs_3_0 PointLightVertexShader();
        pixelShader = compile ps_3_0 PointLightPixelShader();
    }
}