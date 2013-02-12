shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float4 LightNeutralColor;
uniform float2 LightCenter;

uniform float ShadowLength;

float4 ApplyTransform (float2 position2d) {
    float2 localPosition = ((position2d - ViewportPosition) * ViewportScale);
    return mul(mul(float4(localPosition.xy, 0, 1), ModelViewMatrix), ProjectionMatrix);
}

void PointLightVertexShader(
    in float2 position : POSITION0,
    inout float4 color : COLOR0,
    inout float2 lightCenter : TEXCOORD0,
    inout float2 ramp : TEXCOORD1,
    out float2 worldPosition : TEXCOORD2,
    out float4 result : POSITION0
) {
    worldPosition = position;
    result = ApplyTransform(position);
}

void PointLightPixelShader(
    in float2 worldPosition: TEXCOORD2,
    in float2 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    out float4 result : COLOR0
) {
    float distance = length(worldPosition - lightCenter) - ramp.x;
    float distanceOpacity = 1 - clamp(distance / (ramp.y - ramp.x), 0, 1);

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void ShadowVertexShader(
    in float2 position : POSITION0,
    in float pairIndex : BLENDINDICES,
    out float4 result : POSITION0
) {
    float2 direction;

    if (pairIndex == 0) {
        direction = float2(0, 0);
    } else {
        direction = normalize(position - LightCenter);
    }

    result = ApplyTransform(position + (direction * ShadowLength));
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

technique PointLight {
    pass P0
    {
        vertexShader = compile vs_2_0 PointLightVertexShader();
        pixelShader = compile ps_2_0 PointLightPixelShader();
    }
}