#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "ParticleCommon.fxh"

#define PI 3.14159265358979323846

uniform float4 GlobalColor;
uniform float2 Size;
uniform float2 AnimationRate;
uniform float  VelocityRotation;
uniform float  OpacityFromLife;
uniform float  ZToY;

Texture2D BitmapTexture;
sampler BitmapSampler {
    Texture = (BitmapTexture);
    AddressU = WRAP;
    AddressV = WRAP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

uniform float4 BitmapTextureRegion;

static const float3 Corners[] = {
    { -1, -1, 0 },
    { 1, -1, 0 },
    { 1, 1, 0 },
    { -1, 1, 0 }
};

inline float3 ComputeRotatedCorner(
    in int cornerIndex, in float angle
) {    
    float3 corner = Corners[cornerIndex.x] * float3(Size, 1), sinCos;
    sincos(angle, sinCos.x, sinCos.y);
    return float3(
        (sinCos.y * corner.x) - (sinCos.x * corner.y),
        (sinCos.x * corner.x) + (sinCos.y * corner.y),
        corner.z
    );
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

    // FIXME
    result = TransformPosition(
        float4(displayXyz + corner, 1), 0
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
    }
    else {
        angle = (atan2(velocity.y, velocity.x) + PI) * VelocityRotation;
    }
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle);

    VS_Core(
        position, rotatedCorner, cornerIndex,
        result, texCoord
    );

    color = 1;
}

void VS_PosVelAttr(
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

    color = attributes;
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

    VS_Core(
        position, Corners[cornerIndex.x], cornerIndex,
        result, texCoord
    );
}

void PS_Texture (
    in  float2 texCoord : TEXCOORD0,
    in  float4 position : TEXCOORD1,
    out float4 result   : COLOR0
) {
    // FIXME
    float4 texColor = tex2D(BitmapSampler, texCoord);
    if (OpacityFromLife > 0)
        texColor *= clamp(position.w / OpacityFromLife, 0, 1);
    else if (OpacityFromLife < 0)
        texColor *= 1 - clamp(position.w / -OpacityFromLife, 0, 1);

    result = texColor;
    result *= GlobalColor;
    if (result.a <= 0)
        discard;
}

void PS_NoTexture(
    in  float4 color    : COLOR0,
    in  float4 position : TEXCOORD1,
    out float4 result   : COLOR0
) {
    // FIXME
    if (OpacityFromLife > 0)
        result = clamp(position.w / OpacityFromLife, 0, 1);
    else if (OpacityFromLife < 0)
        result = 1 - clamp(position.w / -OpacityFromLife, 0, 1);
    else
        result = 1;

    result *= color;
    result *= GlobalColor;
    if (result.a <= 0)
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