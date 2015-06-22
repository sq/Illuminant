#include "LightCommon.fxh"

#define SHADOW_DEPTH_BIAS -0.01
#define FILLRULE_NEGATIVE_OFFSET 0
#define FILLRULE_POSITIVE_OFFSET 0.99

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float4 LightNeutralColor;
uniform float3 LightCenter;

uniform float3 ShadowLength;

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
    in float2 ramp          : TEXCOORD1, // start, end
    in float2 vpos          : VPOS
) {
    float2 terrainZ = sampleTerrain(vpos);

    /*
    if (lightCenter.z < terrainZ.y)
        discard;
    */

    // FIXME: What about z?
    float3 shadedPixelPosition = float3(worldPosition.xy, terrainZ.y);
    return computeLightOpacity(shadedPixelPosition, lightCenter, ramp.x, ramp.y);
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

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
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

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
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

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
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

    float opacity = color.a;
    float4 lightColorActual = float4(color.r * opacity, color.g * opacity, color.b * opacity, opacity);
    result = lerp(LightNeutralColor, lightColorActual, distanceOpacity);
}

void ShadowVertexShader(
    in float3  position  : POSITION0,
    in float   pairIndex : BLENDINDICES,
    out float2 z         : TEXCOORD0,
    out float4 result    : POSITION0
) {
    float3 delta = (position - LightCenter);
    float3 direction = normalize(delta);
    float3 shadowLengthScaled = float3(ShadowLength.x, ShadowLength.x, ShadowLength.x);

    if (pairIndex == 0) {
        shadowLengthScaled = float3(0, 0, 0);
    }

    float3 untransformed = position + (direction * shadowLengthScaled);

    float3 directionSign = sign(direction);
    float4 fillruleOffset = float4(
        (clamp(directionSign.x,  0, 1) * FILLRULE_POSITIVE_OFFSET) +
        (clamp(directionSign.x, -1, 0) * FILLRULE_NEGATIVE_OFFSET),
        (clamp(directionSign.y,  0, 1) * FILLRULE_POSITIVE_OFFSET) +
        (clamp(directionSign.y, -1, 0) * FILLRULE_NEGATIVE_OFFSET),
        0, 0
    );

    float4 transformed = ApplyTransform(untransformed + fillruleOffset);
    // FIXME: Why do I have to strip Z????
    result = float4(transformed.x, transformed.y, 0, transformed.w);

    z = float2(position.z, untransformed.z);
}

void ShadowPixelShader(
    in  float2 _z        : TEXCOORD0,
    in  float2 vpos      : VPOS,
    out float4 color     : COLOR0
) {
    float startZ = _z.x;
    float endZ   = _z.y + SHADOW_DEPTH_BIAS;

    float2 terrainZ = sampleTerrain(vpos);

    // this is a hack to ensure the portion of the shadow directly beneath
    //  a floating height volume is still shadowed

    // Is this pixel of the shadow ray underneath the height volume that cast the shadow?
    int isDirectlyBeneathFloatingVolume = (startZ >= terrainZ.x);

    // Is this pixel of the shadow ray beneath the terrain, and thus invisible?
    int isBeneathTerrain                = (endZ <= terrainZ.y);

    // Is this pixel lit due to foreshortening of the front side of the height volume?
    int isForeshortened                 = (endZ < terrainZ.x);

    /*
    if (isForeshortened)
        discard;
    */

    if (isDirectlyBeneathFloatingVolume) {
    } else {
        if (isBeneathTerrain)
            discard;
    }

    color = float4(1, 1, 1, 0);
}

technique Shadow {
    pass P0
    {
        vertexShader = compile vs_3_0 ShadowVertexShader();
        pixelShader = compile ps_3_0 ShadowPixelShader();
    }
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