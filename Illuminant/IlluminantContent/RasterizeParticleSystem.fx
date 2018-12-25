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
    AddressV = WRAP;
    MinFilter = POINT;
    MagFilter = POINT;
};

// ramp_strength, ramp_min, ramp_divisor, index_divisor
uniform float4 LifeRampSettings;

uniform float3 RotationAxis;

static const float3 Corners[] = {
    { -1, -1, 0 },
    { 1, -1, 0 },
    { 1, 1, 0 },
    { -1, 1, 0 }
};

inline float3x3 RotationFromAxisAngle (float3 axis, float angle) {
    // angle = angle % (2 * PI);

    float c, s;
    sincos(angle, s, c);

    float x = axis.x;
    float y = axis.y;
    float z = axis.z;
    float xx = x * x;
    float yy = y * y;
    float zz = z * z;
    float xy = x * y;
    float xz = x * z;
    float yz = y * z;

    float3x3 rotationMatrix = {
        xx + (c * (1 - xx)),
        (xy - (c * xy)) + (s * z),
        (xz - (c * xz)) - (s * y),
        (xy - (c * xy)) - (s * z),
        yy + (c * (1 - yy)),
        (yz - (c * yz)) + (s * x),
        (xz - (c * xz)) + (s * y),
        (yz - (c * yz)) - (s * x),
        zz + (c * (1 - zz)),
    };
    return rotationMatrix;
}

inline float3 ComputeRotatedCorner(
    in int cornerIndex, in float angle, in float2 size, in float3 axis, in float3 velocity
) {
    float3 up = axis;
    if (length(velocity) < 0.1) {
        // FIXME
        velocity = float3(0, 0, -1);
    } else {
        velocity = normalize(velocity);
    }
    float3 right = cross(velocity, up);
    float3 orientedUp = cross(velocity, right);

    float3 screenCorner = Corners[cornerIndex.x];
    screenCorner.xy *= size;

    float3 orientedX = screenCorner.x * right;
    float3 orientedY = screenCorner.y * orientedUp;

    /*
    float3x3 rotationMatrix = RotationFromAxisAngle(computedAxis, angle);

    float3 rotatedScreenCorner = mul(screenCorner, rotationMatrix);
    */
    float3 rotatedScreenCorner = orientedX + orientedY;
    return rotatedScreenCorner.xyz;
}

float4 getRampedColorForLifeValueAndIndex (float life, float index) {
    float4 result = getColorForLifeValue(life);
    [branch]
    if (LifeRampSettings.x != 0) {
        float u = (life - LifeRampSettings.y) / LifeRampSettings.z;
        if (LifeRampSettings.x < 0)
            u = 1 - saturate(u);
        float v = index / LifeRampSettings.w;
        float4 rampSample = tex2Dlod(LifeRampSampler, float4(u, v, 0, 0));
        result = lerp(result, rampSample * result, saturate(abs(LifeRampSettings.x)));
    }
    return result;
}

float getRotationForVelocity (float3 velocity) {
    float2 absvel = abs(velocity.xy + float2(0, velocity.z * -ZToY));
    if ((absvel.x < 0.01) && (absvel.y < 0.01))
        return 0;
    else
        return (atan2(velocity.y, velocity.x) + PI) * VelocityRotation;
}

void VS_Core (
    in float4  position,
    in float3  corner,
    in  int2   cornerIndex : BLENDINDICES0, // 0-3
    out float4 result,
    out float2 texCoord
) {
    // HACK: Discard Z
    float3 displayXyz = float3(position.x, position.y - (position.z * ZToY), position.z);

    float3 screenXyz = displayXyz - float3(Viewport.Position.xy, 0) + corner;

    // FIXME
    result = TransformPosition(
        float4(screenXyz.xy * Viewport.Scale.xy, screenXyz.z, 1), 0
    );

    result.z = saturate(result.z);

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

    float angle = getRotationForVelocity(velocity);
    angle += getRotationForLifeAndIndex(position.w, offsetAndIndex.z);
    float2 size = getSizeForLifeValue(position.w);
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle, size, RotationAxis, velocity);

    VS_Core(
        position, rotatedCorner, cornerIndex,
        result, texCoord
    );

    color = getRampedColorForLifeValueAndIndex(position.w, offsetAndIndex.z);
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

    color = attributes * getRampedColorForLifeValueAndIndex(position.w, offsetAndIndex.z);
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
    float3 rotatedCorner = ComputeRotatedCorner(cornerIndex.x, angle, size, RotationAxis, float3(0, 0, 0));

    VS_Core(
        position, rotatedCorner, cornerIndex,
        result, texCoord
    );

    attributes *= getRampedColorForLifeValueAndIndex(position.w, offsetAndIndex.z);
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