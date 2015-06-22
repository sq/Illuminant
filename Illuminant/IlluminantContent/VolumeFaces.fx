#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"

#define MAX_LIGHTS 12
#define NUM_RAYCAST_SAMPLES 16
#define RAYCAST_STEP (1.0 / (NUM_RAYCAST_SAMPLES + 1))
#define MIN_STEP_PX 2

uniform float3 LightPositions    [MAX_LIGHTS];
uniform float3 LightProperties   [MAX_LIGHTS]; // ramp_start, ramp_end, exponential
uniform float4 LightNeutralColors[MAX_LIGHTS];
uniform float4 LightColors       [MAX_LIGHTS];

uniform float  ZToYMultiplier;
uniform int    NumLights;

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

    for (int i = 0; i < NumLights; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];
        float3 lightDirection = float3(worldPosition.xy - lightPosition.xy, 0);

        // Do a stochastic raycast between the light and the wall to determine
        //  whether the light is obstructed.
        // FIXME: This will cause flickering for small obstructions combined
        //  with moving lights.
        if ((length(lightDirection) * RAYCAST_STEP) >= MIN_STEP_PX) {
            // HACK: Start from the wall instead of the light, and step until we find a non-occluded pixel.
            // This handles degenerate geometry like the cylinder-inside-rectangle used to construct pillars
            //  in the test scene.
            float castDistance = 1 - (RAYCAST_STEP * 0.5);
            int obstructed = 0;
            int seenUnobstructed = 0;
            for (int j = 0; j < NUM_RAYCAST_SAMPLES; j++, castDistance -= RAYCAST_STEP) {
                float2 terrainXy = (lightPosition.xy + (lightDirection * castDistance)) * TerrainTextureTexelSize;
                float  terrainZ  = tex2Dgrad(TerrainTextureSampler, terrainXy, 0, 0).r;

                if (terrainZ > lightPosition.z) {
                    if (seenUnobstructed) {
                        obstructed = 1;
                        break;
                    }
                } else {
                    seenUnobstructed = 1;
                }
            }

            if (obstructed)
                break;
        }

        float  opacity = computeLightOpacity(worldPosition, lightPosition, properties.x, properties.y);

        float  lightDotNormal = dot(-normalize(lightDirection), normal);

        // HACK: How do we get smooth ramping here without breaking pure horizontal walls?
        float dotRamp = clamp((lightDotNormal + 0.45) * 1.9, 0, 1);
        opacity *= (dotRamp * dotRamp);

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

    float3 normal = float3(0, 0, 1);

    for (int i = 0; i < NumLights; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];
        float  opacity = computeLightOpacity(worldPosition, lightPosition, properties.x, properties.y);

        float3 lightDirection = normalize(float3(worldPosition.xy - lightPosition.xy, 0));
        float  lightDotNormal = dot(-lightDirection, normal);

        // HACK: How do we get smooth ramping here without breaking pure horizontal walls?
        float dotRamp = clamp((lightDotNormal + 0.45) * 1.9, 0, 1);
        opacity *= (dotRamp * dotRamp);

        /*
        if (worldPosition.z >= lightPosition.z)
            opacity *= 0;
            */

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