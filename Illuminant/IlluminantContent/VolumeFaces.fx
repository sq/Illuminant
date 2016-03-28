#include "..\..\Upstream\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

// UGH
#define MAX_LIGHTS 16

#define SELF_OCCLUSION_HACK 1.5

#define EXPONENTIAL                           0
#define TOP_FACE_RAYCAST_SHADOWS              1
#define FRONT_FACE_RAYCAST_SHADOWS            1

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
    position.xy -= ViewportPosition;
    position.xy *= ViewportScale;

    result = TransformPosition(float4(position.xy, 0, 1), 0);
    result.z = position.z / DistanceFieldExtent.z;
}

float ComputeRaycastedShadowOcclusionSample (
    float3 lightPosition, float2 lightRamp, float3 worldPosition
) {
    return coneTrace(lightPosition, lightRamp, worldPosition);
}

float ComputeRaycastedShadowOpacity (
    float3 lightPosition, float2 lightRamp, float3 worldPosition
) {
    return ComputeRaycastedShadowOcclusionSample(lightPosition, lightRamp, worldPosition);
}

void FrontFacePixelShader (
    inout float4 color : COLOR0,
    in    float3 normal : NORMAL0,
    in    float2 zRange : TEXCOORD0,
    in    float3 worldPosition: TEXCOORD1
) {
    float3 accumulator = float3(0, 0, 0);

    [loop]
    for (int i = 0; i < NumLights; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];

        float  opacity = computeLightOpacity(
            worldPosition, normal,
            lightPosition, properties.x, properties.y
        );
        [branch]
        if (opacity <= 0)
            continue;

#if EXPONENTIAL
        // Conditional exponential ramp
        opacity *= lerp(1, opacity, properties.z);
#endif

#if FRONT_FACE_RAYCAST_SHADOWS
        // HACK: We shift the point forwards along our surface normal to mitigate self-occlusion
        opacity *= ComputeRaycastedShadowOpacity(lightPosition, properties.xy, worldPosition + (normal * SELF_OCCLUSION_HACK));
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
    position.xy -= ViewportPosition;
    position.xy *= ViewportScale;

    result = TransformPosition(float4(position.xy, 0, 1), 0);
    result.z = position.z / DistanceFieldExtent.z;
}

void TopFacePixelShader(
    inout float4 color : COLOR0,
    in    float3 worldPosition : TEXCOORD0
) {
    float3 normal = float3(0, 0, 1);
    float3 accumulator = float3(0, 0, 0);

    // FIXME
    // float2 terrainZ = sampleTerrain(worldPosition.xy);

    [loop]
    for (int i = 0; i < NumLights; i++) {
        // FIXME: What about z?
        float3 properties = LightProperties[i];
        float3 lightPosition = LightPositions[i];

        bool kill = false;

        // if (lightPosition.z < terrainZ.x)
        //    kill = true;
        if (lightPosition.z < worldPosition.z)
            kill = true;

        float  opacity = computeLightOpacity(
            worldPosition, normal,
            lightPosition, properties.x, properties.y
        );
        if (opacity <= 0)
            kill = true;

        [branch]
        if (kill)
            continue;

#if EXPONENTIAL
        // Conditional exponential ramp
        opacity *= lerp(1, opacity, properties.z);
#endif

#if TOP_FACE_RAYCAST_SHADOWS        
        // HACK: We shift the point upwards to mitigate self-occlusion
        opacity *= ComputeRaycastedShadowOpacity(lightPosition, properties.xy, worldPosition + float3(0, 0, SELF_OCCLUSION_HACK));
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