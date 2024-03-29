// Bias and scale the dot product of the light vector and surface normal
// These values produce a very rapid climb up to 1.0 (we don't really want
//  smooth dot attenuation, we're just using it to mask light from behind
//  surfaces)
#define DOT_OFFSET     0.15
#define DOT_RAMP_RANGE 0.15
#define DIRECTIONAL_DOT_OFFSET     0.35
#define DIRECTIONAL_DOT_RAMP_RANGE 0.35
// The final output from the dot computation is raised to this power so
#define DOT_EXPONENT   0.85

#define DEFINE_LightCorners const float3 LightCorners[] = { \
    { 0, 0, 0 }, \
    { 1, 0, 0 }, \
    { 1, 1, 0 }, \
    { 0, 1, 0 } \
};

#include "EnvironmentCommon.fxh"

uniform const float  GBufferViewportRelative;
uniform const float  GBufferInvScaleFactor;
uniform const float4 GBufferTexelSizeAndMisc;

// uniform const float4x4 InverseProjection;

#define GBUFFER_Z_SCALE 1024
#define GBUFFER_Z_OFFSET 1024

#define GetViewportScale GetViewportScalePacked

float2 GetViewportScalePacked () {
    return GBufferTexelSizeAndMisc.zw;
}

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

// returns approximate camera position
// calculates world position data from the gbuffer at the specified screen position
float3 sampleGBuffer (
    float2 screenPositionPx,
    out float3 worldPosition,
    out float3 normal,
    out bool enableShadows,
    out bool fullbright
) {
    enableShadows = true;
    fullbright = false;
    float3 cameraPosition;

    PREFER_BRANCH
    if (any(GBufferTexelSizeAndMisc.xy)) {
        // FIXME: Should we be offsetting distance field samples too?
        float2 sourceXy = screenPositionPx;
        if (GBufferViewportRelative) {
            sourceXy /= GetViewportScale();
            sourceXy += GetViewportPosition();
        }

        float2 uv     = (sourceXy + 0.5) * GBufferTexelSizeAndMisc.xy;
        float4 sample = tex2Dlod(GBufferSampler, float4(uv, 0, 0));

        float relativeY = sample.z;
        float worldZ    = sample.w;
        if (worldZ < 0) {
            // To ensure shadows can be disabled for the ground plane, unshadowed pixels have their Z biased by -1024
            worldZ += 1;
            // Unshadowed pixels have their Z negated
            worldZ = -worldZ;
            enableShadows = false;
        } else if (worldZ >= 9999) {
            // Fullbright pixels have 99999 added to their Z
            worldZ = 0;
            enableShadows = false;
            fullbright = true;
        }
        worldZ *= GBUFFER_Z_SCALE;
        worldZ -= GBUFFER_Z_OFFSET;

        screenPositionPx /= getEnvironmentRenderScale();
        cameraPosition = float3(screenPositionPx.xy, getMaximumZ() + 0.01);

#if 0
            // Reconstruct x/y from inverse projection matrix + z from gbuffer
            float4 reconstructedViewSpacePosition = float4(
                uv.x * 2 - 1,
                (1 - uv.y) * 2 - 1,
                saturate((worldZ - getGroundZ()) / (getMaximumZ() - getGroundZ())),
                1.0
            );
            float4 deprojected = mul(reconstructedViewSpacePosition, InverseProjection);
            worldPosition = (deprojected / deprojected.w).xyz;
            // HACK: Necessary for 2.5D and to maintain exact Z from g-buffer
            worldPosition.y += relativeY;
            // HACK
            worldPosition.xy /= GetViewportScale();
            worldPosition.xy += GetViewportPosition();
            worldPosition.z = worldZ;
#else
            worldPosition = float3(
                (screenPositionPx.xy + float2(0, relativeY)) / GetViewportScale() + GetViewportPosition(),
                worldZ
            );
#endif

        // HACK: Reconstruct the y normal from the z normal
        if (any(sample.xy)) {
            float2 filteredSample = sample.xy;
            normal = decodeNormalSpherical(filteredSample);
        } else {
            // HACK: If the x and y normals are both 0, the normal is intended to be 0 (to disable directional occlusion)
            normal = float3(0, 0, 0);
        }
    } else {
        screenPositionPx /= getEnvironmentRenderScale();
        cameraPosition = float3(screenPositionPx.xy, getMaximumZ() + 0.01);

        worldPosition = float3(
            screenPositionPx.xy / GetViewportScale() + GetViewportPosition(),
            getGroundZ()
        );
        normal = float3(0, 0, 1);
    }

    return cameraPosition;
}

bool checkShadowFilter(float4 evenMoreLightProperties, bool enableShadows) {
    float filter = evenMoreLightProperties.x;
    if (filter < 0)
        return false;
    else 
        return (filter > 0.5) != enableShadows;
}

float computeNormalFactorEx (
    float3 lightNormal, float3 shadedPixelNormal, float offset, float range
) {
    if (!any(shadedPixelNormal))
        return 1;

    float d = dot(-lightNormal, shadedPixelNormal);

    // HACK: We allow the light to be somewhat behind the surface without occluding it,
    //  and we want a smooth ramp between occluded and not-occluded
    return pow(saturate((d + offset) / range), DOT_EXPONENT);
}

float computeNormalFactor(
    float3 lightNormal, float3 shadedPixelNormal
) {
    return computeNormalFactorEx(lightNormal, shadedPixelNormal, DOT_OFFSET, DOT_RAMP_RANGE);
}

float computeSphereLightOpacity (
    float3 shadedPixelPosition, float3 shadedPixelNormal,
    float3 lightCenter, float4 lightProperties, 
    float yDistanceFactor
) {
    float  lightRadius     = lightProperties.x;
    float  lightRampLength = lightProperties.y;
    float  falloffMode     = lightProperties.z;

    float3 distance3      = shadedPixelPosition - lightCenter;
    distance3.y *= yDistanceFactor;
    float  distance       = length(distance3);
    float  distanceFactor = 1 - saturate((distance - lightRadius) / lightRampLength);

    if (getLightOcclusion() > 0) {
        // If the light is too far behind the pixel being shaded, fully occlude it
        // This ensures that we don't get false lighting from behind surfaces when the angle
        //  between the light and the surface becomes too shallow
        distanceFactor *= 1 - saturate(distance3.z / getLightOcclusion());
    }

    float3 lightNormal = distance3 / distance;
    float normalFactor = computeNormalFactor(lightNormal, shadedPixelNormal);

    PREFER_FLATTEN
    if (falloffMode >= 2) {
        distanceFactor = 1 - saturate(distance - lightRadius);
        normalFactor = 1;
    } else if (falloffMode >= 1) {
        distanceFactor *= distanceFactor;
    }

    if (0)
        return (normalFactor * distanceFactor);
    else
        // HACK: If the point is inside the light's radius we ensure it is always fully lit
        return saturate((normalFactor * distanceFactor) + saturate(lightRadius - distance));
}

float CalcSphereLightSpecularity (
    in float3 cameraPosition,
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float3 lightCenter,
    in float power
) {
    float3 lightDirection = shadedPixelPosition - lightCenter;
    float3 h = normalize(normalize(cameraPosition - shadedPixelPosition) - lightDirection);
    return pow(saturate(dot(h, shadedPixelNormal)), power);
}

float computeDirectionalLightOpacity (
    float4 lightDirection, float3 shadedPixelNormal
) {
    if (lightDirection.w < 0.1)
        return 1;
    else
        return computeNormalFactorEx(lightDirection.xyz, shadedPixelNormal, DIRECTIONAL_DOT_OFFSET, DIRECTIONAL_DOT_RAMP_RANGE);
}

void sampleLightProbeBuffer (
    float2 screenPositionPx,
    out float3 worldPosition,
    out float3 normal,
    out float  opacity,
    out float  enableShadows
) {
    float2 uv = screenPositionPx * GBufferTexelSizeAndMisc.xy;
    float4 positionSample = tex2Dlod(GBufferSampler, float4(uv, 0, 0));

    opacity = positionSample.w;
    PREFER_BRANCH
    if (opacity <= 0) {
        discard;
        return;
    }

    worldPosition = positionSample.xyz;
    float4 n = tex2Dlod(LightProbeNormalSampler, float4(uv, 0, 0));
    normal = n.xyz;
    enableShadows = n.w;
}