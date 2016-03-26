#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float4 LightNeutralColor;
uniform float3 LightCenter;

uniform float Time;

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

float PointLightPixelCore(
    in float2 worldPosition : TEXCOORD2,
    in float3 lightCenter   : TEXCOORD0,
    in float2 ramp          : TEXCOORD1, // radius, ramp length
    in float2 vpos          : VPOS
) {
    // FIXME: Restore terrain Z and ground self-occlusion compensation?
    /*
    float2 terrainZ = sampleTerrain(vpos);
    float shadedZ = terrainZ.y;
    */

    float3 shadedPixelPosition = float3(worldPosition.xy, 0);
    float lightOpacity = computeLightOpacity(shadedPixelPosition, lightCenter, ramp.x, ramp.y);
    float tracedOcclusion = coneTrace(lightCenter, ramp, shadedPixelPosition);

    return lightOpacity * tracedOcclusion;
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