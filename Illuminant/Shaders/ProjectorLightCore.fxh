#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define DEBUG_COORDS 0
#define SELF_OCCLUSION_HACK 1.5
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)
#define PROJECTOR_FILTERING LINEAR

sampler ProjectorTextureSampler : register(s5) {
    Texture = (RampTexture);
    AddressU = WRAP;
    AddressV = WRAP;
    MipFilter = PROJECTOR_FILTERING;
    MinFilter = PROJECTOR_FILTERING;
    MagFilter = PROJECTOR_FILTERING;
};

float ProjectorLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float4 mat1, 
    in float4 mat2, 
    in float4 mat3, 
    in float4 mat4,
    // radius, ramp length, texX1, texY1
    in float4 lightProperties,
    // ao radius, texX2, texY2, ao opacity
    in float4 moreLightProperties,
    out float4 projectorSpacePosition
) {
    float4 coneLightProperties = lightProperties;

    float4x4 invMatrix = float4x4(
        mat1, mat2, mat3, mat4
    );
    projectorSpacePosition = mul(float4(shadedPixelPosition, 1), invMatrix);
    // Offset into texture region
    projectorSpacePosition.xy += lightProperties.zw;
    // If the projector space position drops below 0 on the z axis just force it up to 0 since the light would hit
    //  the ground
    projectorSpacePosition.z = max(0, projectorSpacePosition.z);

    coneLightProperties.z = 0;
    coneLightProperties.w = 0;

    float constantOpacity = lightProperties.x;

    float distanceOpacity = 1;
    // If lamp is clamped, apply distance falloff
    if (!DEBUG_COORDS) {
        float2 sz = moreLightProperties.yz - lightProperties.zw;
        float3 clampedPosition = clamp3(projectorSpacePosition, float3(lightProperties.zw, 0), float3(moreLightProperties.yz, 1));
        float threshold = 0.001;
        float distanceToVolume = min(length(clampedPosition - projectorSpacePosition), threshold) * (1 / threshold);

        if (lightProperties.y > 0.5)
            distanceOpacity = max(1 - distanceToVolume, 0);
    }

    bool visible = (distanceOpacity > 0) && 
        (shadedPixelPosition.x > -9999) &&
        (constantOpacity > 0);

    clip(visible ? 1 : -1);

    // Optionally clamp to texture region
    projectorSpacePosition.xy = lerp(projectorSpacePosition.xy, clamp(projectorSpacePosition.xy, lightProperties.zw, moreLightProperties.yz), lightProperties.y);

    // Zero out y/z before we pass them into AO
    moreLightProperties.y = 0;
    moreLightProperties.z = 0;

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    float preTraceOpacity = distanceOpacity * aoOpacity;

    // FIXME: Projector shadows?
    /*
    bool traceShadows = visible && lightProperties.w && (preTraceOpacity >= SHADOW_OPACITY_THRESHOLD);
    float coneOpacity = lineConeTrace(
        startPosition, endPosition, u,
        coneLightProperties.xy, 
        float2(getConeGrowthFactor(), moreLightProperties.y),
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
        vars, traceShadows
    );
    */
    float coneOpacity = 1;

    float lightOpacity = preTraceOpacity;
    lightOpacity *= coneOpacity;
    lightOpacity *= constantOpacity;

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}

void ProjectorLightVertexShader(
    in int2 vertexIndex              : BLENDINDICES0,
    inout float4 mat1                : TEXCOORD0, 
    inout float4 mat2                : TEXCOORD1, 
    inout float4 mat3                : TEXCOORD4, 
    inout float4 mat4                : TEXCOORD5,
    // radius, ramp length, texX1, texY1
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, texX2, texY2, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    out float3 worldPosition         : POSITION1,
    out float4 result                : POSITION0
) {
    DEFINE_LightCorners

    float3 vertex = LightCorners[vertexIndex.x];
    // FIXME: Projector bounding box
    float3 tl = -9999, br = 9999;

    worldPosition = lerp(tl, br, vertex);

    // FIXME: z offset

    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float4 ProjectorLightColorCore(
    float4 projectorSpacePosition,
    float opacity
) {
    if (DEBUG_COORDS) {
        return float4(clamp(projectorSpacePosition.xyz, 0, 1), 1);
    }

    projectorSpacePosition.z = 0;
    projectorSpacePosition.w = 0;
    float4 texColor = tex2Dlod(ProjectorTextureSampler, projectorSpacePosition);

    return float4(texColor.rgb * texColor.a * opacity, 1);
}