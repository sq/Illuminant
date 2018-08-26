// Bias and scale the dot product of the light vector and surface normal
// These values produce a very rapid climb up to 1.0 (we don't really want
//  smooth dot attenuation, we're just using it to mask light from behind
//  surfaces)
#define DOT_OFFSET     0.1
#define DOT_RAMP_RANGE 0.1
// The final output from the dot computation is raised to this power so
#define DOT_EXPONENT   0.85

#define RELATIVEY_SCALE 128

static const half3 LightCorners[] = {
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
    half2 screenPositionPx,
    out half3 worldPosition,
    out half3 normal
) {
    [branch]
    if (any(GBufferTexelSize)) {
        // FIXME: Should we be offsetting distance field samples too?
        half2 uv     = (screenPositionPx + 0.5) * GBufferTexelSize;
        half4 sample = tex2Dlod(GBufferSampler, float4(uv, 0, 0));

        half relativeY = sample.z * RELATIVEY_SCALE;
        half worldZ    = sample.w * 512;

        screenPositionPx /= Environment.RenderScale;

        worldPosition = half3(
            (screenPositionPx.xy + float2(0, relativeY)) / Viewport.Scale.xy + Viewport.Position.xy,
            worldZ
        );

        // HACK: Reconstruct the y normal from the z normal
        half normalZ = (sample.y - 0.5) * 2;
        normal = normalize(half3(
            (sample.x - 0.5) * 2, 1 - abs(normalZ), normalZ
        ));
    } else {
        screenPositionPx /= Environment.RenderScale;

        worldPosition = half3(
            screenPositionPx.xy / Viewport.Scale.xy + Viewport.Position.xy,
            getGroundZ()
        );
        normal = half3(0, 0, 1);
    }
}

float computeNormalFactor (
    half3 lightNormal, half3 shadedPixelNormal
) {
    if (!any(shadedPixelNormal))
        return 1;

    half d = dot(-lightNormal, shadedPixelNormal);

    // HACK: We allow the light to be somewhat behind the surface without occluding it,
    //  and we want a smooth ramp between occluded and not-occluded
    return pow(clamp((d + DOT_OFFSET) / DOT_RAMP_RANGE, 0, 1), DOT_EXPONENT);
}

float computeSphereLightOpacity (
    half3 shadedPixelPosition, half3 shadedPixelNormal,
    float3 lightCenter, float4 lightProperties, 
    half yDistanceFactor, out bool distanceFalloff 
) {
    half  lightRadius     = lightProperties.x;
    half  lightRampLength = lightProperties.y;
    half  falloffMode     = lightProperties.z;

    half3 distance3      = shadedPixelPosition - lightCenter;
    distance3.y *= yDistanceFactor;
    half  distance       = length(distance3);
    half  distanceFactor = 1 - clamp((distance - lightRadius) / lightRampLength, 0, 1);

    half3 lightNormal = distance3 / distance;
    half normalFactor = computeNormalFactor(lightNormal, shadedPixelNormal);

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

half computeDirectionalLightOpacity (
    half3 lightDirection, half3 shadedPixelNormal
) {
    half  normalFactor = computeNormalFactor(lightDirection, shadedPixelNormal);
    return normalFactor;
}

void sampleLightProbeBuffer (
    float2 screenPositionPx,
    out half3 worldPosition,
    out half4 normal,
    out half  opacity
) {
    half2 uv = screenPositionPx * GBufferTexelSize;
    half4 positionSample = tex2Dlod(GBufferSampler, half4(uv, 0, 0));

    opacity = positionSample.w;
    [branch]
    if (opacity <= 0) {
        discard;
        return;
    }

    worldPosition = positionSample.xyz;
    normal = tex2Dlod(LightProbeNormalSampler, half4(uv, 0, 0));
}