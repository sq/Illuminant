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

void DirectionalLightVertexShader(
    in float2    position            : POSITION0,
    inout float4 color               : COLOR0,
    inout float4 lightDirectionAndAO : TEXCOORD0,
    inout float2 lightProperties     : TEXCOORD1,
    out float2   worldPosition       : TEXCOORD2,
    out float4   result              : POSITION0
) {
    worldPosition = position;
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, 0));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float DirectionalLightPixelCore(
    in float2 worldPosition       : TEXCOORD2,
    in float4 lightDirectionAndAO : TEXCOORD0,
    in float2 lightProperties     : TEXCOORD1,
    in float2 vpos                : VPOS
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float lightOpacity = computeDirectionalLightOpacity(lightDirectionAndAO.xyz, shadedPixelNormal);
    bool visible = (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    [branch]
    if ((lightDirectionAndAO.w >= 0.5) && visible) {
        float distance = sampleDistanceField(shadedPixelPosition, vars);
        float aoRamp = clamp(distance / lightDirectionAndAO.w, 0, 1);
        lightOpacity *= aoRamp;
    }

    bool traceShadows = (visible && lightProperties.x);

    // FIXME: Cone trace for directional shadows?
    [branch]
    if (traceShadows) {
        float3 fakeLightCenter = shadedPixelPosition - (lightDirectionAndAO.xyz * lightProperties.y);
        float2 fakeRamp = float2(9999, 0);
        lightOpacity *= coneTrace(fakeLightCenter, fakeRamp, shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal), vars);
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

void DirectionalLightPixelShader(
    in  float2 worldPosition       : TEXCOORD2,
    in  float4 lightDirectionAndAO : TEXCOORD0,
    in  float2 lightProperties     : TEXCOORD1,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float opacity = DirectionalLightPixelCore(
        worldPosition, lightDirectionAndAO, lightProperties, vpos
    );

    float4 lightColorActual = float4(color.rgb * color.a * opacity, 1);
    result = lightColorActual;
}

technique DirectionalLight {
    pass P0
    {
        vertexShader = compile vs_3_0 DirectionalLightVertexShader();
        pixelShader  = compile ps_3_0 DirectionalLightPixelShader();
    }
}