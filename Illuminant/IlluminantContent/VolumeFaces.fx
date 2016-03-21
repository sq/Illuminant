#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define MAX_LIGHTS 12

#define EXPONENTIAL                           0
#define TOP_FACE_DOT_RAMP                     0
#define TOP_FACE_RAYCAST_SHADOWS              1
#define FRONT_FACE_DOT_RAMP                   1
#define FRONT_FACE_RAYCAST_SHADOWS            1

// Never do a raycast when the light is closer to the wall than this
#define RAYCAST_MIN_DISTANCE_PX 4
// If the light contribution is lower than this, don't raycast
#define RAYCAST_MIN_OPACITY (1.0 / 255) * 4

uniform float3 LightPositions    [MAX_LIGHTS];
uniform float3 LightProperties   [MAX_LIGHTS]; // ramp_start, ramp_end, exponential
uniform float4 LightNeutralColors[MAX_LIGHTS];
uniform float4 LightColors       [MAX_LIGHTS];

uniform int    NumLights;

void FrontFaceVertexShader (
    in    float3 position      : POSITION0, // x, y, z
    inout float3 normal        : NORMAL0,
    inout float2 zRange        : TEXCOORD0,
    out   float3 worldPosition : TEXCOORD1,
    out   float4 result        : POSITION0
) {
    worldPosition = position.xyz;
    position.y -= ZToYMultiplier * position.z;
    result = TransformPosition(float4(position.xy, 0, 1), 0);
    result.z = position.z;
}

float ComputeRaycastedShadowOcclusionSample (
    float3 lightPosition, float3 worldPosition
) {
    float3 lightDirection = worldPosition - lightPosition;
    float maxDistance = length(lightDirection);
    lightDirection = normalize(lightDirection);

    // Do a raycast between the light and the wall to determine
    //  whether the light is obstructed.
    // FIXME: This will cause flickering for small obstructions combined
    //  with moving lights.
    int seenUnobstructed = 0, obstructed = 0;
    if (
        (maxDistance >= RAYCAST_MIN_DISTANCE_PX)
    ) {
        float castDistance = 0.1;
        float closestProximity = 999;

        while (castDistance < maxDistance) {
            float3 samplePosition = (lightPosition + (lightDirection * castDistance));
            float2 terrainZ = sampleTerrain(samplePosition);

            // Only obstruct the ray if it passes through the interior of the height volume.
            // This allows volumes to float above the ground and look okay.
            if (
                (terrainZ.x < samplePosition.z) &&
                (terrainZ.y > samplePosition.z)
            ) {
                obstructed = 1;
            }
            else if (obstructed) {
                seenUnobstructed = 1;
            }

            float distanceToObstacle = sampleDistanceField(samplePosition.xy);
            closestProximity = min(distanceToObstacle, closestProximity);

            castDistance += max(abs(distanceToObstacle), 1);
        }

        // if (obstructed && seenUnobstructed)
        if (closestProximity >= 24)
            return 0;
        else
            return clamp((24 - closestProximity) / 20, 0, 1);
    }

    return 0;
}

float ComputeRaycastedShadowOpacity (
    float3 lightPosition, float3 worldPosition
) {
    float3 pos = lightPosition + ((worldPosition - lightPosition) / 2);
    float s = sampleDistanceField(pos.xy);
    return clamp(s / 32, 0, 1);

    return 1.0 - ComputeRaycastedShadowOcclusionSample(lightPosition, worldPosition);
}

void FrontFacePixelShader (
    inout float4 color : COLOR0,
    in    float3 normal : NORMAL0,
    in    float2 zRange : TEXCOORD0,
    in    float3 worldPosition: TEXCOORD1
) {
    float3 accumulator = float3(0, 0, 0);

    for (int i = 0; i < NumLights; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];
        float3 lightDirection = worldPosition - lightPosition;

        float maxDistance = length(lightDirection);
        lightDirection = normalize(lightDirection);

        float  opacity = computeLightOpacity(worldPosition, lightPosition, properties.x, properties.y);
        if (opacity <= 0)
            continue;

#if EXPONENTIAL
        // Conditional exponential ramp
        opacity *= lerp(1, opacity, properties.z);
#endif

#if FRONT_FACE_DOT_RAMP
        float  lightDotNormal = dot(-lightDirection, normal);

        // HACK: How do we get smooth ramping here without breaking pure horizontal walls?
        float dotRamp = clamp((lightDotNormal + 0.5) * 2, 0, 1);
        opacity *= dotRamp;
#endif

#if FRONT_FACE_RAYCAST_SHADOWS
        opacity *= ComputeRaycastedShadowOpacity(lightPosition, worldPosition);
#endif

        float4 lightColor = lerp(LightNeutralColors[i], LightColors[i], opacity);
        accumulator += lightColor.rgb * lightColor.a;
    }

    color = float4(accumulator, 1.0);
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
    float3 normal = float3(0, 0, 1);
    float3 accumulator = float3(0, 0, 0);

    for (int i = 0; i < NumLights; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];

        if (lightPosition.z < worldPosition.z)
            continue;

        float  opacity = computeLightOpacity(worldPosition, lightPosition, properties.x, properties.y);
        if (opacity <= 0)
            continue;

#if EXPONENTIAL
        // Conditional exponential ramp
        opacity *= lerp(1, opacity, properties.z);
#endif

#if TOP_FACE_DOT_RAMP
        float3 lightDirection = normalize(float3(worldPosition.xy - lightPosition.xy, 0));
        float  lightDotNormal = dot(-lightDirection, normal);

        // HACK: How do we get smooth ramping here without breaking pure horizontal walls?
        float dotRamp = clamp((lightDotNormal + 0.45) * 1.9, 0, 1);
        opacity *= (dotRamp * dotRamp);
#endif

#if TOP_FACE_RAYCAST_SHADOWS        
        opacity *= ComputeRaycastedShadowOpacity(lightPosition, worldPosition);
#endif

        float4 lightColor = lerp(LightNeutralColors[i], LightColors[i], opacity);
        accumulator += lightColor.rgb * lightColor.a;
    }

    color = float4(accumulator, 1.0);
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