#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"

#define SELF_OCCLUSION_HACK 1.1

uniform float Time;

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(Viewport.Position.xy, 0)) * float3(Viewport.Scale, 1));
    localPosition.xy *= Environment.RenderScale;
    return mul(mul(float4(localPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
}

void SphereLightVertexShader(
    in float2 position               : POSITION0,
    inout float4 color               : COLOR0,
    inout float3 lightCenter         : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float3 moreLightProperties : TEXCOORD3,
    out float2 worldPosition         : TEXCOORD2,
    out float4 result                : POSITION0
) {
    worldPosition = position;
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, lightCenter.z));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float SphereLightPixelCore(
    in float2 worldPosition       : TEXCOORD2,
    in float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties     : TEXCOORD1,
    // ao radius, distance falloff, y falloff factor
    in float3 moreLightProperties : TEXCOORD3,
    in float2 vpos                : VPOS
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
        lightCenter, lightProperties, moreLightProperties.z,
        distanceCull
    );

    bool visible = (!distanceCull) && 
        (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    [branch]
    if ((moreLightProperties.x >= 0.5) && visible) {
        float distance = sampleDistanceField(shadedPixelPosition, vars);
        float aoRamp = clamp(distance / moreLightProperties.x, 0, 1);
        lightOpacity *= aoRamp;
    }

    bool traceShadows = visible && lightProperties.w && (lightOpacity >= 1 / 256.0);

    [branch]
    if (traceShadows) {
        lightOpacity *= coneTrace(
            lightCenter, lightProperties.xy, 
            float2(DistanceField.ConeGrowthFactor, moreLightProperties.y),
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
    in  float2 worldPosition       : TEXCOORD2,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float3 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float opacity = SphereLightPixelCore(
        worldPosition, lightCenter, lightProperties, moreLightProperties, vpos
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