#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define SELF_OCCLUSION_HACK 1.1

uniform float Time;

void SphereLightVertexShader(
    in float2 cornerWeight           : POSITION0,
    inout float4 color               : COLOR0,
    inout float3 lightCenter         : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float4 moreLightProperties : TEXCOORD3,
    out float3 worldPosition         : TEXCOORD2,
    out float4 result                : POSITION0
) {
    float  radius = lightProperties.x + lightProperties.y + 1;
    float3 radius3 = float3(radius, radius, 0);
    float3 tl = lightCenter - radius3, br = lightCenter + radius3;
    tl.y -= radius * getInvZToYMultiplier();
    worldPosition = lerp(tl, br, float3(cornerWeight, 0));

    float3 screenPosition = (worldPosition - float3(Viewport.Position.xy, 0));
    screenPosition.xy *= Viewport.Scale * Environment.RenderScale;
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

void SphereLightProbeVertexShader(
    in float2 cornerWeight           : POSITION0,
    inout float4 color               : COLOR0,
    inout float3 lightCenter         : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float4 moreLightProperties : TEXCOORD3,
    out float4 result                : POSITION0
) {
    float2 clipPosition = float2(cornerWeight.x > 0 ? 999 : -999, cornerWeight.y > 0 ? 999 : -999);

    result = float4(clipPosition.xy, 0, 1);
}

float SphereLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    in float4 lightProperties     : TEXCOORD1,
    // ao radius, distance falloff, y falloff factor, ao opacity
    in float4 moreLightProperties : TEXCOORD3,
    in bool   useDistanceRamp,
    in bool   useOpacityRamp
) {
    bool  distanceCull = false;
    float lightOpacity = computeSphereLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, lightProperties, moreLightProperties.z,
        distanceCull
    );

    bool visible = (!distanceCull) && 
        (shadedPixelPosition.x > -9999);

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    [branch]
    if (useDistanceRamp)
        lightOpacity = SampleFromRamp(lightOpacity);

    computeAO(lightOpacity, shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    bool traceShadows = visible && lightProperties.w && (lightOpacity >= 1 / 256.0);

    [branch]
    if (traceShadows) {
        lightOpacity *= coneTrace(
            lightCenter, lightProperties.xy, 
            float2(getConeGrowthFactor(), moreLightProperties.y),
            shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
            vars
        );
    }

    [branch]
    if (useOpacityRamp)
        lightOpacity = SampleFromRamp(lightOpacity);

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
    in  float3 worldPosition       : TEXCOORD2,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightWithDistanceRampPixelShader(
    in  float3 worldPosition       : TEXCOORD2,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, true, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightWithOpacityRampPixelShader(
    in  float3 worldPosition       : TEXCOORD2,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, true
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightProbePixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, false, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightProbeWithDistanceRampPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, true, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightProbeWithOpacityRampPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, false, true
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

technique SphereLight {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightPixelShader();
    }
}

technique SphereLightWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightWithDistanceRampPixelShader();
    }
}

technique SphereLightWithOpacityRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader = compile ps_3_0 SphereLightWithOpacityRampPixelShader();
    }
}

technique SphereLightProbe {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbePixelShader();
    }
}

technique SphereLightProbeWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbeWithDistanceRampPixelShader();
    }
}

technique SphereLightProbeWithOpacityRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbeWithOpacityRampPixelShader();
    }
}