#include "RampCommon.fxh"

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float4 LightNeutralColor;
uniform float3 LightCenter;

uniform float3 ShadowLength;

uniform float4 ClipPlanes[6];

float4 ApplyTransform (float2 position2d) {
    float2 localPosition = ((position2d - ViewportPosition) * ViewportScale);
    return mul(mul(float4(localPosition.xy, 0, 1), ModelViewMatrix), ProjectionMatrix);
}

void PointLightVertexShader(
    in float2 position : POSITION0,
    inout float4 color : COLOR0,
    inout float3 lightCenter : TEXCOORD0,
    inout float2 ramp : TEXCOORD1,
    out float2 worldPosition : TEXCOORD2,
    out float4 result : POSITION0
) {
    worldPosition = position;
    result = ApplyTransform(position);
}

void PointLightPixelShaderLinear(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    out float4 result : COLOR0
) {
    // FIXME: What about z?
    float distance = length(worldPosition - lightCenter.xy) - ramp.x;
    float distanceOpacity = 1 - clamp(distance / (ramp.y - ramp.x), 0, 1);

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void PointLightPixelShaderExponential(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    out float4 result : COLOR0
) {
    // FIXME: What about z?
    float distance = length(worldPosition - lightCenter.xy) - ramp.x;
    float distanceOpacity = 1 - clamp(distance / (ramp.y - ramp.x), 0, 1);
    distanceOpacity *= distanceOpacity;

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void PointLightPixelShaderLinearRampTexture(
    in float2 worldPosition: TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    out float4 result : COLOR0
) {
    // FIXME: What about z?
    float distance = length(worldPosition - lightCenter.xy) - ramp.x;
    float distanceOpacity = 1 - clamp(distance / (ramp.y - ramp.x), 0, 1);

    distanceOpacity = RampLookup(distanceOpacity);

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void PointLightPixelShaderExponentialRampTexture(
    in float2 worldPosition: TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    out float4 result : COLOR0
) {
    // FIXME: What about z?
    float distance = length(worldPosition - lightCenter.xy) - ramp.x;
    float distanceOpacity = 1 - clamp(distance / (ramp.y - ramp.x), 0, 1);
    distanceOpacity *= distanceOpacity;

    distanceOpacity = RampLookup(distanceOpacity);

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void ShadowVertexShader(
    in float3 position : POSITION0,
    in float pairIndex : BLENDINDICES,
    out float4 result : POSITION0
) {
    float3 direction;

    if (pairIndex == 0) {
        direction = float3(0, 0, 0);
    } else {
        direction = normalize(position - LightCenter);
    }

    // FIXME: Why isn't this right?
    /*
    float shadowLengthScaled =
        ShadowLength * max(1 / abs(direction.x), 1 / abs(direction.y));
    */
    float shadowLengthScaled = ShadowLength;

    float3 untransformed = position + (direction * shadowLengthScaled);
    // FIXME: What about Z?
    result = ApplyTransform(untransformed.xy);
}

void ShadowPixelShader(
    out float4 color : COLOR0
) {
    color = float4(0, 0, 0, 0);
}

technique Shadow {
    pass P0
    {
        vertexShader = compile vs_2_0 ShadowVertexShader();
        pixelShader = compile ps_2_0 ShadowPixelShader();
    }
}

technique PointLightLinear {
    pass P0
    {
        vertexShader = compile vs_2_0 PointLightVertexShader();
        pixelShader = compile ps_2_0 PointLightPixelShaderLinear();
    }
}

technique PointLightExponential {
    pass P0
    {
        vertexShader = compile vs_2_0 PointLightVertexShader();
        pixelShader = compile ps_2_0 PointLightPixelShaderExponential();
    }
}

technique PointLightLinearRampTexture {
    pass P0
    {
        vertexShader = compile vs_2_0 PointLightVertexShader();
        pixelShader = compile ps_2_0 PointLightPixelShaderLinearRampTexture();
    }
}

technique PointLightExponentialRampTexture {
    pass P0
    {
        vertexShader = compile vs_2_0 PointLightVertexShader();
        pixelShader = compile ps_2_0 PointLightPixelShaderExponentialRampTexture();
    }
}