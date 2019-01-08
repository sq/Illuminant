#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\GeometryCommon.fxh"
#include "Bezier.fxh"
#include "ParticleCommon.fxh"

struct RasterizeParticleSystemSettings {
    float4 GlobalColor;
    float4 BitmapTextureRegion;
    float2 SizeFactor;
};

uniform RasterizeParticleSystemSettings RasterizeSettings;
uniform ClampedBezier1 RoundingPowerFromLife;
uniform bool Rounded, BitmapBilinear, ColumnFromVelocity, RowFromVelocity;

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

    sincos(angle, sinCos.x, sinCos.y);

    return float3(
        (sinCos.y * corner.x) - (sinCos.x * corner.y),
        (sinCos.x * corner.x) + (sinCos.y * corner.y),
        corner.z
    );
}

void VS_PosVelAttr(
    in  float2 xy                    : POSITION0,
    in  float3 offsetAndIndex        : POSITION1,
    in  int2   cornerIndex           : BLENDINDICES0, // 0-3
    out float4 result                : POSITION0,
    out float2 texCoord              : TEXCOORD0,
    out float3 positionXyAndRounding : TEXCOORD1,
    out float4 renderData            : TEXCOORD2,
    out float4 color                 : COLOR0
) {
    float4 actualXy = float4(xy + offsetAndIndex.xy, 0, 0);
    float4 position;
    readStateUv(actualXy, position, renderData, color);

    float life = position.w;
    if ((life <= 0) || stippleReject(offsetAndIndex.z)) {
        result = float4(0, 0, 0, 0);
        return;
    }

    float angle = renderData.y;
    angle = fmod(angle, 2 * PI);

    float2 size = renderData.x * System.TexelAndSize.zw * RasterizeSettings.SizeFactor;
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle * getVelocityRotation(), size);
    float2 positionXy = Corners[cornerIndex.x];

    // HACK: Discard Z
    float3 displayXyz = float3(position.x, position.y - (position.z * getZToY()), 0);

    float3 screenXyz = displayXyz - float3(Viewport.Position.xy, 0) + rotatedCorner;

    // FIXME
    result = TransformPosition(
        float4(screenXyz.xy * Viewport.Scale.xy, screenXyz.z, 1), 0
    );

    float2 cornerCoord = (Corners[cornerIndex.x].xy / 2) + 0.5;
    texCoord = lerp(RasterizeSettings.BitmapTextureRegion.xy, RasterizeSettings.BitmapTextureRegion.zw, cornerCoord);

    float2 texSize = (RasterizeSettings.BitmapTextureRegion.zw - RasterizeSettings.BitmapTextureRegion.xy);
    float2 frameCountXy = floor(1.0 / texSize);
    float2 frameIndexXy = floor(getAnimationRate() * position.w);

    float  frameAngle = angle;
    float2 maxAngleXy = (2 * PI) / frameCountXy;
    float2 frameFromVelocity = round(frameAngle / maxAngleXy);

    frameIndexXy.y += floor(renderData.w);
    if (ColumnFromVelocity)
        frameIndexXy.x += frameFromVelocity.x;
    if (RowFromVelocity)
        frameIndexXy.y += frameFromVelocity.y;

    frameIndexXy.x = max(frameIndexXy.x, 0) % frameCountXy.x;
    frameIndexXy.y = clamp(frameIndexXy.y, 0, frameCountXy.y - 1);

    float2 frameTexCoord = frameIndexXy * texSize;
    texCoord += frameTexCoord;

    positionXyAndRounding = float3(
        positionXy, evaluateBezier1(RoundingPowerFromLife, position.w)
    );
}

float computeCircularAlpha (float3 positionXyAndRounding) {
    float2 position = positionXyAndRounding;
    if (Rounded) {
        float distance = length(position);
        float power = max(positionXyAndRounding.z, 0.01);
        float divisor = saturate(1 - power);
        float distanceFromEdge = saturate(distance - power) / divisor;
        float powDistanceFromEdge = pow(distanceFromEdge, power);
        return saturate(1 - powDistanceFromEdge);
    } else
        return 1;
}

void PS_Texture (
    in  float4 color                 : COLOR0,
    in  float2 texCoord              : TEXCOORD0,
    in  float3 positionXyAndRounding : TEXCOORD1,
    out float4 result                : COLOR0
) {
    // FIXME
    result = color;    
    if (color.a > (1 / 512)) {
        float4 texColor = tex2D(BitmapSampler, texCoord);
        result *= texColor;
        result *= RasterizeSettings.GlobalColor;
    }
    result *= computeCircularAlpha(positionXyAndRounding);
    if (result.a <= (1 / 512))
        discard;
}

void PS_TexturePoint(
    in  float4 color                 : COLOR0,
    in  float2 texCoord              : TEXCOORD0,
    in  float3 positionXyAndRounding : TEXCOORD1,
    out float4 result                : COLOR0
) {
    // FIXME
    result = color;
    if (color.a > (1 / 512)) {
        float4 texColor = tex2D(BitmapPointSampler, texCoord);
        result *= texColor;
        result *= RasterizeSettings.GlobalColor;
    }
    result *= computeCircularAlpha(positionXyAndRounding);
    if (result.a <= (1 / 512))
        discard;
}

void PS_NoTexture (
    in  float4 color                 : COLOR0,
    in  float3 positionXyAndRounding : TEXCOORD1,
    out float4 result                : COLOR0
) {
    result = color;
    result *= RasterizeSettings.GlobalColor;
    result *= computeCircularAlpha(positionXyAndRounding);
    if (result.a <= (1 / 512))
        discard;
}

technique TextureLinear {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttr();
        pixelShader = compile ps_3_0 PS_Texture();
    }
}

technique TexturePoint {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttr();
        pixelShader = compile ps_3_0 PS_TexturePoint();
    }
}

technique NoTexture {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_PosVelAttr();
        pixelShader = compile ps_3_0 PS_NoTexture();
    }
}