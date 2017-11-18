#include "..\..\..\Fracture\Squared\RenderLib\Content\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Content\GeometryCommon.fxh"
#include "ParticleCommon.fxh"

#define PI 3.14159265358979323846

uniform float2 SourceCoordinateOffset;

uniform float2 Size;
uniform float2 AnimationRate;
uniform float  VelocityRotation;
uniform float  OpacityFromLife;

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

void vs (
    in  float2 xy          : POSITION0,
    in  float2 offset      : POSITION1,
    in  int2   cornerIndex : BLENDINDICES0, // 0-3
    out float4 result      : POSITION0,
    out float2 texCoord    : TEXCOORD0,
    out float4 position    : TEXCOORD1,
    out float4 velocity    : TEXCOORD2,
    out float4 attributes  : COLOR0
) {
    float2 actualXy = xy + SourceCoordinateOffset + offset;
    readState(actualXy, position, velocity, attributes);

    float life = position.w;
    if (life <= 0)
        return;

    float angle = (atan2(velocity.y, velocity.x) + PI) * VelocityRotation;
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle);

    // FIXME
    result = TransformPosition(
        float4(position.xyz + rotatedCorner, 1), 0
    );

    texCoord = (Corners[cornerIndex.x].xy / 2) + 0.5;
    texCoord = lerp(BitmapTextureRegion.xy, BitmapTextureRegion.zw, texCoord);

    texCoord += (BitmapTextureRegion.zw - BitmapTextureRegion.xy) * floor(AnimationRate * life);
}

void PS_White (
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
}

void PS_AttributeColor (
    in  float4 color    : COLOR0,
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
    result = texColor * color;
}

technique White {
    pass P0
    {
        vertexShader = compile vs_3_0 vs();
        pixelShader = compile ps_3_0 PS_White();
    }
}

technique AttributeColor {
    pass P0
    {
        vertexShader = compile vs_3_0 vs();
        pixelShader = compile ps_3_0 PS_AttributeColor();
    }
}
