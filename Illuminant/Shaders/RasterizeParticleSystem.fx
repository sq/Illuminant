#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
// #include "Bezier.fxh"
#include "ParticleCommon.fxh"

uniform bool   Rounded, BitmapBilinear;
uniform float2 SizeFactor;
uniform float  RoundingPower;
uniform float4 GlobalColor;
uniform float4 BitmapTextureRegion;

Texture2D BitmapTexture;
sampler BitmapSampler {
    Texture = (BitmapTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};
sampler BitmapPointSampler {
    Texture = (BitmapTexture);
    AddressU = CLAMP;
    AddressV = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

static const float3 Corners[] = {
    { -1, -1, 0 },
    { 1, -1, 0 },
    { 1, 1, 0 },
    { -1, 1, 0 }
};

inline float3 ComputeRotatedCorner (
    in int cornerIndex, in float angle, in float2 size
) {    
    float3 corner = Corners[cornerIndex.x] * float3(size, 1), sinCos;

    angle = fmod(angle, 2 * PI);
    sincos(angle, sinCos.x, sinCos.y);

    return float3(
        (sinCos.y * corner.x) - (sinCos.x * corner.y),
        (sinCos.x * corner.x) + (sinCos.y * corner.y),
        corner.z
    );
}

void VS_PosVelAttr(
    in  float2 xy             : POSITION0,
    in  float3 offsetAndIndex : POSITION1,
    in  int2   cornerIndex    : BLENDINDICES0, // 0-3
    out float4 result         : POSITION0,
    out float2 texCoord       : TEXCOORD0,
    out float2 positionXy     : TEXCOORD1,
    out float4 renderData     : TEXCOORD2,
    out float4 color          : COLOR0
) {
    float4 actualXy = float4(xy + offsetAndIndex.xy, 0, 0);
    float4 position;
    readStateUv(actualXy, position, renderData, color);

    float life = position.w;
    if ((life <= 0) || stippleReject(offsetAndIndex.z)) {
        result = float4(0, 0, 0, 0);
        return;
    }

    float angle = renderData.z;
    float2 size = renderData.xy * SizeFactor;
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle, size);
    positionXy = Corners[cornerIndex.x];

    // HACK: Discard Z
    float3 displayXyz = float3(position.x, position.y - (position.z * getZToY()), 0);

    float3 screenXyz = displayXyz - float3(Viewport.Position.xy, 0) + rotatedCorner;

    // FIXME
    result = TransformPosition(
        float4(screenXyz.xy * Viewport.Scale.xy, screenXyz.z, 1), 0
    );

    float2 cornerCoord = (Corners[cornerIndex.x].xy / 2) + 0.5;
    texCoord = lerp(BitmapTextureRegion.xy, BitmapTextureRegion.zw, cornerCoord);

    float2 texSize = (BitmapTextureRegion.zw - BitmapTextureRegion.xy);
    float2 frameCountXy = floor(1.0 / texSize);
    float2 frameIndexXy = floor((getAnimationRate() * position.w) % frameCountXy);

    texCoord += (frameIndexXy * texSize);
}

float computeCircularAlpha (float2 position) {
    if (Rounded) {
        float distance = length(position);
        float power = max(RoundingPower, 0.01);
        float divisor = saturate(1 - power);
        float distanceFromEdge = saturate(distance - power) / divisor;
        float powDistanceFromEdge = pow(distanceFromEdge, power);
        return saturate(1 - powDistanceFromEdge);
    } else
        return 1;
}

void PS_Texture (
    in  float4 color      : COLOR0,
    in  float2 texCoord   : TEXCOORD0,
    in  float2 positionXy : TEXCOORD1,
    out float4 result     : COLOR0
) {
    // FIXME
    result = color;    
    if (color.a > (1 / 512)) {
        float4 texColor = tex2D(BitmapSampler, texCoord);
        result *= texColor;
        result *= GlobalColor;
    }
    result *= computeCircularAlpha(positionXy);
    if (result.a <= (1 / 512))
        discard;
}

void PS_TexturePoint(
    in  float4 color      : COLOR0,
    in  float2 texCoord : TEXCOORD0,
    in  float2 positionXy : TEXCOORD1,
    out float4 result : COLOR0
) {
    // FIXME
    result = color;
    if (color.a > (1 / 512)) {
        float4 texColor = tex2D(BitmapPointSampler, texCoord);
        result *= texColor;
        result *= GlobalColor;
    }
    result *= computeCircularAlpha(positionXy);
    if (result.a <= (1 / 512))
        discard;
}

void PS_NoTexture (
    in  float4 color      : COLOR0,
    in  float2 positionXy : TEXCOORD1,
    out float4 result     : COLOR0
) {
    result = color;
    result *= GlobalColor;
    result *= computeCircularAlpha(positionXy);
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

technique AttributeColorPoint {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttr();
        pixelShader = compile ps_3_0 PS_TexturePoint();
    }
}

technique AttributeColorNoTexture {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttr();
        pixelShader = compile ps_3_0 PS_NoTexture();
    }
}