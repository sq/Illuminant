#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"

#define SELF_OCCLUSION_HACK 1.1
static const float OpacityThreshold = (0.5 / 255.0);

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float Time;

Texture2D LightBinTexture : register(t7);
sampler   LightBinSampler : register(s7) {
    Texture = (LightBinTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

uniform float2 LightBinTextureSize;


float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(ViewportPosition.xy, 0)) * float3(ViewportScale, 1));
    return mul(mul(float4(localPosition.xyz, 1), ModelViewMatrix), ProjectionMatrix);
}

void SphereLightVertexShader(
    in float2 position              : POSITION0,
    inout float4 color              : COLOR0,
    inout float3 lightCenter        : TEXCOORD0,
    inout float3 rampAndExponential : TEXCOORD1,
    out float2 worldPosition        : TEXCOORD2,
    out float4 result               : POSITION0
) {
    worldPosition = position;
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, 0));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float SphereLightPixelCore(
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

    float lightOpacity = computeSphereLightOpacity(
        shadedPixelPosition, shadedPixelNormal,
        lightCenter, ramp.x, ramp.y, exponential
    );

    float tracedOcclusion = 1;

    /*
    [branch]
    if (lightOpacity >= OpacityThreshold) {
        tracedOcclusion = coneTrace(lightCenter, ramp, shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal));
    }
    */

    return lightOpacity * tracedOcclusion;
}

void SphereLightPixelShader(
    in float2 worldPosition      : TEXCOORD2,
    in float3 lightCenter        : TEXCOORD0,
    in float3 rampAndExponential : TEXCOORD1, // start, end, exp
    in float4 color              : COLOR0,
    in  float2 vpos              : VPOS,
    out float4 result            : COLOR0
) {
    float opacity = SphereLightPixelCore(
        lightCenter, rampAndExponential.xy, vpos, rampAndExponential.z
    );

    if (opacity < OpacityThreshold) {
        discard;
        result = 0;
    } {
        float4 lightColorActual = float4(color.rgb * color.a * opacity, color.a * opacity);
        result = lightColorActual;
    }
}

void LightBinVertexShader(
    in    float2 position   : POSITION0,
    inout float  lightCount : TEXCOORD0,
    inout float  binIndex   : TEXCOORD1,
    out   float4 result     : POSITION0
) {
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, 0));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

void LightBinPixelShader(
    in  float  lightCount : TEXCOORD0,
    in  float  binIndex   : TEXCOORD1,
    in  float2 vpos       : VPOS,
    out float4 result     : COLOR0
) {
    float2 texelSize = 1.0 / LightBinTextureSize;

    result = 0;
    float v = texelSize.y * binIndex;

    [loop]
    for (float i = 0; i < lightCount; i++) {
        float4 uv = float4(i * texelSize.x * 3, v, 0, 0);

        float3 lightCenter = tex2Dlod(LightBinSampler, uv).xyz;
        uv.x += texelSize.x;

        float3 rampAndExponential = tex2Dlod(LightBinSampler, uv).xyz;
        uv.x += texelSize.x;

        float4 lightColor = tex2Dlod(LightBinSampler, uv);

        float opacity = SphereLightPixelCore(
            lightCenter, rampAndExponential.xy, vpos, rampAndExponential.z
        );

        float4 lightColorActual = float4(
            lightColor.rgb * lightColor.a * opacity, 
            lightColor.a * opacity
        );

        result += lightColorActual;
    }

    /*
    if (result.a < OpacityThreshold) {
        discard;
        result = 0;
    }
    */
}

technique SphereLight {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightPixelShader();
    }
}

technique LightBin {
    pass P0
    {
        vertexShader = compile vs_3_0 LightBinVertexShader();
        pixelShader  = compile ps_3_0 LightBinPixelShader();
    }
}