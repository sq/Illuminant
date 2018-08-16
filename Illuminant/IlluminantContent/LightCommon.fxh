// Bias and scale the dot product of the light vector and surface normal
// These values produce a very rapid climb up to 1.0 (we don't really want
//  smooth dot attenuation, we're just using it to mask light from behind
//  surfaces)
#define DOT_OFFSET     0.1
#define DOT_RAMP_RANGE 0.1
// The final output from the dot computation is raised to this power so
#define DOT_EXPONENT   0.85

static const float3 LightCorners[] = {
    { 0, 0, 0 },
    { 1, 0, 0 },
    { 1, 1, 0 },
    { 0, 1, 0 }
};

#include "EnvironmentCommon.fxh"

uniform float  GBufferInvScaleFactor;
uniform float2 GBufferTexelSize;

Texture2D GBuffer      : register(t2);
sampler GBufferSampler : register(s2) {
    Texture = (GBuffer);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

Texture2D ProbeNormals : register(t4);
sampler LightProbeNormalSampler : register(s4) {
    Texture = (ProbeNormals);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

// returns world position data from the gbuffer at the specified screen position
void sampleGBuffer (
    float2 screenPositionPx,
    out float3 worldPosition,
    out float3 normal
) {
    [branch]
    if (any(GBufferTexelSize)) {
        // FIXME: Should we be offsetting distance field samples too?
        float2 uv     = (screenPositionPx + 0.5) * GBufferTexelSize;
        float4 sample = tex2Dlod(GBufferSampler, float4(uv, 0, 0));

        float relativeY = sample.z * 512;
        float worldZ    = sample.w * 512;

        screenPositionPx /= Environment.RenderScale;

        worldPosition = float3(
            (screenPositionPx.xy + float2(0, relativeY)) / Viewport.Scale.xy + Viewport.Position.xy,
            worldZ
        );

        // HACK: Reconstruct the y normal from the z normal
        float normalZ = (sample.y - 0.5) * 2;
        normal = normalize(float3(
            (sample.x - 0.5) * 2, 1 - abs(normalZ), normalZ
        ));
    } else {
        screenPositionPx /= Environment.RenderScale;

        worldPosition = float3(
            screenPositionPx.xy / Viewport.Scale.xy + Viewport.Position.xy,
            getGroundZ()
        );
        normal = float3(0, 0, 1);
    }
}

float computeNormalFactor (
    float3 lightNormal, float3 shadedPixelNormal
) {
    if (!any(shadedPixelNormal))
        return 1;

    float d = dot(-lightNormal, shadedPixelNormal);

    // HACK: We allow the light to be somewhat behind the surface without occluding it,
    //  and we want a smooth ramp between occluded and not-occluded
    return pow(clamp((d + DOT_OFFSET) / DOT_RAMP_RANGE, 0, 1), DOT_EXPONENT);
}

float computeSphereLightOpacity (
    float3 shadedPixelPosition, float3 shadedPixelNormal,
    float3 lightCenter, float4 lightProperties, 
    float yDistanceFactor, out bool distanceFalloff 
) {
    float  lightRadius     = lightProperties.x;
    float  lightRampLength = lightProperties.y;
    float  falloffMode     = lightProperties.z;

    float3 distance3      = shadedPixelPosition - lightCenter;
    distance3.y *= yDistanceFactor;
    float  distance       = length(distance3);
    float  distanceFactor = 1 - clamp((distance - lightRadius) / lightRampLength, 0, 1);

    float3 lightNormal = distance3 / distance;
    float normalFactor = computeNormalFactor(lightNormal, shadedPixelNormal);

    [flatten]
    if (falloffMode >= 2) {
        distanceFactor = 1 - clamp(distance - lightRadius, 0, 1);
        normalFactor = 1;
    } else if (falloffMode >= 1) {
        distanceFactor *= distanceFactor;
    }

    distanceFalloff = (distanceFactor <= 0);

    return normalFactor * distanceFactor;
}

float computeDirectionalLightOpacity (
    float3 lightDirection, float3 shadedPixelNormal
) {
    float  normalFactor = computeNormalFactor(lightDirection, shadedPixelNormal);
    return normalFactor;
}

void sampleLightProbeBuffer (
    float2 screenPositionPx,
    out float3 worldPosition,
    out float4 normal,
    out float  opacity
) {
    float2 uv = screenPositionPx * GBufferTexelSize;
    float4 positionSample = tex2Dlod(GBufferSampler, float4(uv, 0, 0));

    opacity = positionSample.w;
    [branch]
    if (opacity <= 0) {
        discard;
        return;
    }

    worldPosition = positionSample.xyz;
    normal = tex2Dlod(LightProbeNormalSampler, float4(uv, 0, 0));
}