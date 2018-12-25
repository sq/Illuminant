#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "ParticleCommon.fxh"

#define PI 3.14159265358979323846

uniform float4 GlobalColor;
uniform float2 AnimationRate;
uniform float  VelocityRotation;
uniform float  ZToY;

Texture2D BitmapTexture;
sampler BitmapSampler {
    Texture = (BitmapTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

uniform float4 BitmapTextureRegion;

Texture2D LifeRampTexture;
sampler LifeRampSampler {
    Texture = (LifeRampTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MinFilter = POINT;
    MagFilter = POINT;
};

// ramp_strength, ramp_min, ramp_divisor
uniform float3 LifeRampSettings;

static const float3 Corners[] = {
    { -1, -1, 0 },
    { 1, -1, 0 },
    { 1, 1, 0 },
    { -1, 1, 0 }
};

inline float3 ComputeRotatedCorner(
    in int cornerIndex, in float angle, in float2 size
) {    
    float3 corner = Corners[cornerIndex.x] * float3(size, 1), sinCos;
    sincos(angle, sinCos.x, sinCos.y);
    return float3(
        (sinCos.y * corner.x) - (sinCos.x * corner.y),
        (sinCos.x * corner.x) + (sinCos.y * corner.y),
        corner.z
    );
}

float4 getRampedColorForLifeValue (float life) {
    float4 result = getColorForLifeValue(life);
    [branch]
    if (LifeRampSettings.x != 0) {
        float u = (life - LifeRampSettings.y) / LifeRampSettings.z;
        if (LifeRampSettings.x < 0)
            u = 1 - saturate(u);
        float4 rampSample = tex2Dlod(LifeRampSampler, float4(u, 0, 0, 0));
        result = lerp(result, rampSample * result, saturate(abs(LifeRampSettings.x)));
    }
    return result;
}

void VS_Core (
    in float4  position,
    in float3  corner,
    in  int2   cornerIndex : BLENDINDICES0, // 0-3
    out float4 result,
    out float2 texCoord
) {
    // HACK: Discard Z
    float3 displayXyz = float3(position.x, position.y - (position.z * ZToY), 0);

    float3 screenXyz = displayXyz - float3(Viewport.Position.xy, 0) + corner;

    // FIXME
    result = TransformPosition(
        float4(screenXyz.xy * Viewport.Scale.xy, screenXyz.z, 1), 0
    );

    texCoord = (Corners[cornerIndex.x].xy / 2) + 0.5;
    texCoord = lerp(BitmapTextureRegion.xy, BitmapTextureRegion.zw, texCoord);

    texCoord += (BitmapTextureRegion.zw - BitmapTextureRegion.xy) * floor(AnimationRate * position.w);
}

void VS_PosVelAttrWhite(
    in  float2 xy          : POSITION0,
    in  float3 offsetAndIndex : POSITION1,
    in  int2   cornerIndex : BLENDINDICES0, // 0-3
    out float4 result : POSITION0,
    out float2 texCoord : TEXCOORD0,
    out float4 position : TEXCOORD1,
    out float4 velocity : TEXCOORD2,
    out float4 attributes : TEXCOORD3,
    out float4 color : COLOR0
) {
    float4 actualXy = float4(xy + offsetAndIndex.xy, 0, 0);
    readStateUv(actualXy, position, velocity, attributes);

    float life = position.w;
    if ((life <= 0) || stippleReject(offsetAndIndex.z)) {
        result = float4(0, 0, 0, 0);
        return;
    }

    float2 absvel = abs(velocity.xy + float2(0, velocity.z * -ZToY));
    float angle;
    if ((absvel.x < 0.01) && (absvel.y < 0.01)) {
        angle = 0;
    } else {
        angle = (atan2(velocity.y, velocity.x) + PI) * VelocityRotation;
    }
    angle += getRotationForLifeAndIndex(position.w, offsetAndIndex.z);
    float2 size = getSizeForLifeValue(position.w);
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle, size);

    VS_Core(
        position, rotatedCorner, cornerIndex,
        result, texCoord
    );

    color = getRampedColorForLifeValue(position.w);
}

void VS_PosVelAttr(
    in  float2 xy             : POSITION0,
    in  float3 offsetAndIndex : POSITION1,
    in  int2   cornerIndex    : BLENDINDICES0, // 0-3
    out float4 result         : POSITION0,
    out float2 texCoord       : TEXCOORD0,
    out float4 position       : TEXCOORD1,
    out float4 velocity       : TEXCOORD2,
    out float4 attributes     : TEXCOORD3,
    out float4 color          : COLOR0
) {
    VS_PosVelAttrWhite(
        xy,
        offsetAndIndex,
        cornerIndex,
        result,
        texCoord,
        position,
        velocity,
        attributes,
        color
    );

    color = attributes * getRampedColorForLifeValue(position.w);
}

void VS_PosAttr (
    in  float2 xy          : POSITION0,
    in  float3 offsetAndIndex : POSITION1,
    in  int2   cornerIndex : BLENDINDICES0, // 0-3
    out float4 result      : POSITION0,
    out float2 texCoord    : TEXCOORD0,
    out float4 position    : TEXCOORD1,
    out float4 attributes  : COLOR0
) {
    float2 actualXy = xy + offsetAndIndex.xy;
    position = tex2Dlod(PositionSampler, float4(actualXy, 0, 0));
    attributes = tex2Dlod(AttributeSampler, float4(actualXy, 0, 0));

    float life = position.w;
    if ((life <= 0) || stippleReject(offsetAndIndex.z)) {
        result = float4(0, 0, 0, 0);
        return;
    }

    float angle = getRotationForLifeAndIndex(position.w, offsetAndIndex.z);
    float2 size = getSizeForLifeValue(position.w);
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle, size);

    VS_Core(
        position, rotatedCorner, cornerIndex,
        result, texCoord
    );

    attributes *= getRampedColorForLifeValue(position.w);
}

void PS_Texture (
    in  float4 color    : COLOR0,
    in  float2 texCoord : TEXCOORD0,
    in  float4 position : TEXCOORD1,
    out float4 result   : COLOR0
) {
    // FIXME
    result = color;
    if (color.a > (1 / 512)) {
        float4 texColor = tex2D(BitmapSampler, texCoord);
        result *= texColor;
        result *= GlobalColor;
    }
    if (result.a <= (1 / 512))
        discard;
}

void PS_NoTexture(
    in  float4 color    : COLOR0,
    in  float4 position : TEXCOORD1,
    out float4 result   : COLOR0
) {
    result = color;
    result *= GlobalColor;
    if (result.a <= (1 / 512))
        discard;
}

technique AttributeColor {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttr();
        pixelShader = compile ps_3_0 PS_Texture();
    }
}

technique White {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttrWhite();
        pixelShader = compile ps_3_0 PS_Texture();
    }
}

technique AttributeColorNoTexture {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttr();
        pixelShader = compile ps_3_0 PS_NoTexture();
    }
}

technique WhiteNoTexture {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttrWhite();
        pixelShader = compile ps_3_0 PS_NoTexture();
    }
}