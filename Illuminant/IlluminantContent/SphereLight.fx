#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"

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

void SphereLightVertexShader(
    in float2 position            : POSITION0,
    inout float4 color            : COLOR0,
    inout float4 lightCenterAndAO : TEXCOORD0,
    inout float4 lightProperties  : TEXCOORD1,
    out float2 worldPosition      : TEXCOORD2,
    out float4 result             : POSITION0
) {
    worldPosition = position;
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, lightCenterAndAO.z));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float SphereLightPixelCore(
    in float2 worldPosition    : TEXCOORD2,
    in float4 lightCenterAndAO : TEXCOORD0,
    in float4 lightProperties  : TEXCOORD1, // radius, ramp length, ramp mode, enable shadows
    in float2 vpos             : VPOS
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    bool  distanceCull = false;
    float lightOpacity = computeSphereLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        lightCenterAndAO.xyz, lightProperties,
        distanceCull
    );

    bool visible = (!distanceCull) && 
        (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    [branch]
    if ((lightCenterAndAO.w >= 0.5) && visible) {
        float distance = sampleDistanceField(shadedPixelPosition, vars);
        float aoRamp = clamp(distance / lightCenterAndAO.w, 0, 1);
        lightOpacity *= aoRamp;
    }

    bool traceShadows = visible && lightProperties.w && (lightOpacity >= 1 / 256.0);

    [branch]
    if (traceShadows) {
        lightOpacity *= coneTrace(
            lightCenterAndAO.xyz, lightProperties.xy, 
            DistanceField.ConeGrowthFactor,
            shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
            vars
        );
    }

    [branch]
    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    if (visible) {
        return lightOpacity;
    } else {
        discard;
        return 0;
    }
}

void SphereLightPixelShader(
    in  float2 worldPosition    : TEXCOORD2,
    in  float4 lightCenterAndAO : TEXCOORD0,
    in  float4 lightProperties  : TEXCOORD1,
    in  float4 color            : COLOR0,
    in  float2 vpos             : VPOS,
    out float4 result           : COLOR0
) {
    float opacity = SphereLightPixelCore(
        worldPosition, lightCenterAndAO, lightProperties, vpos
    );

    float4 lightColorActual = float4(color.rgb * color.a * opacity, 1);
    result = lightColorActual;
}

technique SphereLight {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightPixelShader();
    }
}