shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;

float4 ApplyTransform (float2 position2d) {
    float2 localPosition = ((position2d - ViewportPosition) * ViewportScale);
    return mul(float4(localPosition.xy, 0, 1), ProjectionMatrix);
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
    float distance = length(worldPosition - lightCenter);
    float distanceOpacity = 1.0 - clamp((distance - ramp.x) / (ramp.y - ramp.x), 0, 1);
    float opacity = color.a * distanceOpacity;
    result = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
}

const float shadowLength = 99999;

void ShadowVertexShader(
    in float2 a : POSITION0,
    in float2 b : POSITION1,
    in float2 light : POSITION2,
    in int cornerIndex : BLENDINDICES,
    out float4 result : POSITION0
) {
    float2 origin, direction;

    if (cornerIndex == 0) {
        origin = a;
        direction = float2(0, 0);
    } else if (cornerIndex == 1) {
        origin = a;
        direction = normalize(a - light);
    } else if (cornerIndex == 2) {
        origin = b;
        direction = float2(0, 0);
    } else {
        origin = b;
        direction = normalize(b - light);
    }

    result = ApplyTransform(origin + (direction * shadowLength));
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