#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"

#define NUM_LIGHTS 8

uniform float3 LightPositions    [NUM_LIGHTS];
uniform float3 LightProperties   [NUM_LIGHTS]; // ramp_start, ramp_end, exponential
uniform float4 LightNeutralColors[NUM_LIGHTS];
uniform float4 LightColors       [NUM_LIGHTS];

uniform float  ZToYMultiplier;

void FrontFaceVertexShader (
    in    float3 position      : POSITION0, // x, y, z
    inout float3 normal        : NORMAL0,
    out   float3 worldPosition : TEXCOORD0,
    out   float4 result        : POSITION0
) {
    worldPosition = position.xyz;
    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy, 0, 1), 0);
    result.z = position.z;
}

void FrontFacePixelShader (
    inout float4 color : COLOR0,
    in    float3 normal : NORMAL0,
    in    float3 worldPosition: TEXCOORD0
) {
    color = float4(0, 0, 0, 0);

    for (int i = 0; i < NUM_LIGHTS; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];
        float  opacity = computeLightOpacity(worldPosition, lightPosition, properties.x, properties.y);

        float3 lightDirection = normalize(float3(worldPosition.xy - lightPosition.xy, 0));
        float  lightDotNormal = dot(lightDirection, normal);

        if (lightDotNormal > 0)
            opacity *= 0;
        if (worldPosition.y >= lightPosition.y)
            opacity *= 0;

        opacity *= lerp(1, opacity, properties.z);
        float4 lightColor = lerp(LightNeutralColors[i], LightColors[i], opacity);

        color += lightColor;
    }

    color.a = 1.0f;
}

void TopFaceVertexShader(
    in  float3 position      : POSITION0, // x, y, z
    out float3 worldPosition : TEXCOORD0,
    out float4 result        : POSITION0
) {
    worldPosition = position.xyz;
    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy, 0, 1), 0);
    result.z = position.z;
}

void TopFacePixelShader(
    inout float4 color : COLOR0,
    in    float3 worldPosition : TEXCOORD0
) {
    color = float4(0, 0, 0, 0);

    for (int i = 0; i < NUM_LIGHTS; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];
        float  opacity = computeLightOpacity(worldPosition, lightPosition, properties.x, properties.y);

        if (worldPosition.z >= lightPosition.z)
            opacity *= 0;

        opacity *= lerp(1, opacity, properties.z);
        float4 lightColor = lerp(LightNeutralColors[i], LightColors[i], opacity);

        color += lightColor;
    }

    color.a = 1.0f;
}

technique VolumeFrontFace
{
    pass P0
    {
        vertexShader = compile vs_3_0 FrontFaceVertexShader();
        pixelShader  = compile ps_3_0 FrontFacePixelShader();
    }
}

technique VolumeTopFace
{
    pass P0
    {
        vertexShader = compile vs_3_0 TopFaceVertexShader();
        pixelShader  = compile ps_3_0 TopFacePixelShader();
    }
}