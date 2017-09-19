#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"

#define SELF_OCCLUSION_HACK 1.1

uniform float Time;

float4 ApplyTransform (float3 position) {
    return mul(mul(float4(position.xyz, 1), Viewport.ModelView), Viewport.Projection);
}

void DirectionalLightVertexShader(
    in float2    position            : POSITION0,
    inout float4 color               : COLOR0,
    inout float3 lightDirection      : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float3 moreLightProperties : TEXCOORD3,
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
    in float3 lightDirection      : TEXCOORD0,
    // enableShadows, shadowTraceLength, shadowSoftness, shadowRampRate
    in float4 lightProperties     : TEXCOORD1,
    // aoRadius, shadowDistanceFalloff, shadowRampLength
    in float3 moreLightProperties : TEXCOORD3,
    in float2 vpos                : VPOS
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float lightOpacity = computeDirectionalLightOpacity(lightDirection, shadedPixelNormal);
    bool visible = (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    [branch]
    if ((moreLightProperties.x >= 0.5) && visible) {
        float distance = sampleDistanceField(shadedPixelPosition, vars);
        float aoRamp = clamp(distance / moreLightProperties.x, 0, 1);
        lightOpacity *= aoRamp;
    }

    bool traceShadows = visible && lightProperties.x && (lightOpacity >= 1 / 256.0);

    // FIXME: Cone trace for directional shadows?
    [branch]
    if (traceShadows) {
        float3 fakeLightCenter = shadedPixelPosition - (lightDirection * lightProperties.y);
        float2 fakeRamp = float2(lightProperties.z, moreLightProperties.y);
        lightOpacity *= coneTrace(
            fakeLightCenter, fakeRamp, 
            float2(lightProperties.w, moreLightProperties.y),
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

void DirectionalLightPixelShader(
    in  float2 worldPosition       : TEXCOORD2,
    in  float3 lightDirection      : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float3 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float opacity = DirectionalLightPixelCore(
        worldPosition, lightDirection, lightProperties, moreLightProperties, vpos
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