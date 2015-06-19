#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"

#define NUM_LIGHTS 8

uniform float3 LightPositions    [NUM_LIGHTS];
uniform float3 LightProperties   [NUM_LIGHTS]; // ramp_start, ramp_end, exponential
uniform float4 LightNeutralColors[NUM_LIGHTS];
uniform float4 LightColors       [NUM_LIGHTS];

uniform float  ZToYMultiplier;

void ScreenSpaceVertexShader (
    in  float3 position       : POSITION0, // x, y, z
    out float3 worldPosition : TEXCOORD0,
    out float4 result        : POSITION0
) {
    worldPosition = position.xyz;
    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy, 0, 1), 0);
}

void FrontFacePixelShader (
    inout float4 color : COLOR0,
    in    float3 worldPosition: TEXCOORD0
) {
    color = float4(0, 0, 0, 0);

    for (int i = 0; i < NUM_LIGHTS; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];
        float  opacity = computeLightOpacity(worldPosition, lightPosition, properties.x, properties.y);

        if (worldPosition.y >= lightPosition.y)
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
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader  = compile ps_3_0 FrontFacePixelShader();
    }
}