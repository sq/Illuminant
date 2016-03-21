#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float4 LightNeutralColor;
uniform float3 LightCenter;

#define PENUMBRA_THICKNESS 8

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(ViewportPosition.xy, 0)) * float3(ViewportScale, 1));
    return mul(mul(float4(localPosition.xyz, 1), ModelViewMatrix), ProjectionMatrix);
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
    // FIXME: Z
    float4 transformedPosition = ApplyTransform(float3(position, lightCenter.z));
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float coneTrace (
    in float3 lightCenter,
    in float3 shadedPixelPosition
) {
    float traceOffset = 0;
    float3 traceVector = (shadedPixelPosition - lightCenter);
    float traceLength = length(traceVector);
    traceVector = normalize(traceVector);

    float lowestDistance = 999;
    float coneAttenuation = 1.0;

    while (traceOffset < traceLength) {
        float3 tracePosition = lightCenter + (traceVector * traceOffset);
        float distanceToObstacle = sampleDistanceField(tracePosition.xy);

        if (distanceToObstacle <= 0) {
            // TODO: Factor in Z
            /*
            float2 obstacleZ = sampleTerrain(tracePosition.xy);

            if ((obstacleZ.y >= lightCenter.z) && (obstacleZ.y >= shadedZ))
            */

            return 0;
        }

        float maxSearch = traceLength - traceOffset;
        float stepAttenuation = min(PENUMBRA_THICKNESS, maxSearch);
        coneAttenuation = min(coneAttenuation, clamp(distanceToObstacle / stepAttenuation, 0, 1));

        lowestDistance = min(distanceToObstacle, lowestDistance);

        traceOffset += max(abs(distanceToObstacle), 1);
    }

    return coneAttenuation;
}

float PointLightPixelCore(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter   : TEXCOORD0,
    in float2 ramp          : TEXCOORD1, // start, end
    in float2 vpos          : VPOS
) {
    float2 terrainZ = sampleTerrain(vpos);

    float shadedZ = terrainZ.y;

    if ((lightCenter.z < terrainZ.x) && (lightCenter.z < terrainZ.y))
        shadedZ = GroundZ;

    float3 shadedPixelPosition = float3(worldPosition.xy, shadedZ);

    // FIXME: What about z?
    float lightOpacity = computeLightOpacity(shadedPixelPosition, lightCenter, ramp.x, ramp.y);

    return lightOpacity * coneTrace(lightCenter, shadedPixelPosition);
}

void PointLightPixelShaderLinear(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float distanceOpacity = PointLightPixelCore(
        worldPosition, lightCenter, ramp, vpos
    );

    float4 lightColorActual = float4(color.rgb * color.a, color.a);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void PointLightPixelShaderExponential(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float distanceOpacity = PointLightPixelCore(
        worldPosition, lightCenter, ramp, vpos
    );

    distanceOpacity *= distanceOpacity;

    float4 lightColorActual = float4(color.rgb * color.a, color.a);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void PointLightPixelShaderLinearRampTexture(
    in float2 worldPosition: TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float distanceOpacity = PointLightPixelCore(
        worldPosition, lightCenter, ramp, vpos
    );

    distanceOpacity = RampLookup(distanceOpacity);

    float4 lightColorActual = float4(color.rgb * color.a, color.a);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void PointLightPixelShaderExponentialRampTexture(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter : TEXCOORD0,
    in float2 ramp : TEXCOORD1, // start, end
    in float4 color : COLOR0,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    float distanceOpacity = PointLightPixelCore(
        worldPosition, lightCenter, ramp, vpos
    );

    distanceOpacity *= distanceOpacity;
    distanceOpacity = RampLookup(distanceOpacity);

    float4 lightColorActual = float4(color.rgb * color.a, color.a);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

technique PointLightLinear {
    pass P0
    {
        vertexShader = compile vs_3_0 PointLightVertexShader();
        pixelShader = compile ps_3_0 PointLightPixelShaderLinear();
    }
}

technique PointLightExponential {
    pass P0
    {
        vertexShader = compile vs_3_0 PointLightVertexShader();
        pixelShader = compile ps_3_0 PointLightPixelShaderExponential();
    }
}

technique PointLightLinearRampTexture {
    pass P0
    {
        vertexShader = compile vs_3_0 PointLightVertexShader();
        pixelShader = compile ps_3_0 PointLightPixelShaderLinearRampTexture();
    }
}

technique PointLightExponentialRampTexture {
    pass P0
    {
        vertexShader = compile vs_3_0 PointLightVertexShader();
        pixelShader = compile ps_3_0 PointLightPixelShaderExponentialRampTexture();
    }
}