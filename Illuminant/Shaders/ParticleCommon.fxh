#if !DEFINED_RTD
#define DEFINED_RTD 1
// FIXME: Can we do a relative shader include here somehow?
#define ACCEPTS_VPOS in float2 __vpos__ : VPOS
#define RAW_VPOS __vpos__.xy

uniform const float2 __RenderTargetDimensions__;

#if FNA
#define GET_VPOS normalize_vpos(__vpos__)

float2 normalize_vpos (float2 __vpos__) {
    float2 result = RAW_VPOS;
    if (__RenderTargetDimensions__.y < 0)
        result.y = -__RenderTargetDimensions__.y - result.y;
    return floor(result);
}
#else
#define GET_VPOS __vpos__
#endif

float2 GetRenderTargetSize () {
    return __RenderTargetDimensions__;
}
// FIXME
#endif

#define PI 3.14159265358979323846
#define VelocityConstantScale 1000

#ifndef PARTICLE_SYSTEM_DEFINED
#define PARTICLE_SYSTEM_DEFINED

struct ParticleSystemSettings {
    // deltaTimeSeconds, friction, maximumVelocity, lifeDecayRate
    float4 GlobalSettings;
    // escapeVelocity, bounceVelocityMultiplier, collisionDistance, collisionLifePenalty
    float4 CollisionSettings;
    float4 TexelAndSize;
    // rate_x, rate_y, velocityRotation, zToY
    float4 AnimationRateAndRotationAndZToY;
};

uniform ParticleSystemSettings System;
uniform const float StippleFactor;

inline float2 getAnimationRate () {
    return System.AnimationRateAndRotationAndZToY.xy;
}

inline float getVelocityRotation () {
    return System.AnimationRateAndRotationAndZToY.z;
}

inline float getZToY () {
    return System.AnimationRateAndRotationAndZToY.w;
}

float getDeltaTimeSeconds () {
    return System.GlobalSettings.x / VelocityConstantScale;
}

float getDeltaTime () {
    return System.GlobalSettings.x;
}

float getFriction () {
    return System.GlobalSettings.y;
}

float getMaximumVelocity () {
    return System.GlobalSettings.z;
}

float getLifeDecayRate () {
    return System.GlobalSettings.w;
}

float getEscapeVelocity () {
    return System.CollisionSettings.x;
}

float getBounceVelocityMultiplier () {
    return System.CollisionSettings.y;
}

float getCollisionDistance () {
    return System.CollisionSettings.z;
}

float getCollisionLifePenalty () {
    return System.CollisionSettings.w;
}

float2 getTexel () {
    return System.TexelAndSize.xy;
}

Texture2D PositionTexture;
sampler PositionSampler {
    Texture = (PositionTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

Texture2D VelocityTexture;
sampler VelocitySampler {
    Texture = (VelocityTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

Texture2D AttributeTexture;
sampler AttributeSampler {
    Texture = (AttributeTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

void VS_Update (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0
) {
    result = float4(xy.x, xy.y, 0, 1);
}

void readStateUv (
    in float4 uv,
    out float4 position,
    out float4 velocity,
    out float4 attributes
) {
    position = tex2Dlod(PositionSampler, uv);
    velocity = tex2Dlod(VelocitySampler, uv);
    attributes = tex2Dlod(AttributeSampler, uv);
}

void readState (
    in float2 xy,
    out float4 position,
    out float4 velocity,
    out float4 attributes
) {
    float4 uv = float4(xy * getTexel(), 0, 0);
    readStateUv(uv, position, velocity, attributes);
}

void readStatePV (
    in float2 xy,
    out float4 position,
    out float4 velocity
) {
    float4 uv = float4(xy * getTexel(), 0, 0);
    position = tex2Dlod(PositionSampler, uv);
    velocity = tex2Dlod(VelocitySampler, uv);
}

void readStateOrDiscard (
    in float2 xy,
    out float4 position,
    out float4 velocity,
    out float4 attributes
) {
    float4 uv = float4(xy * getTexel(), 0, 0);
    position = tex2Dlod(PositionSampler, uv);

    // To support occlusion queries and reduce bandwidth used by dead particles
    if (position.w <= 0) {
        velocity = 0;
        attributes = 1;
        discard;
        return;
    }

    velocity = tex2Dlod(VelocitySampler, uv);
    attributes = tex2Dlod(AttributeSampler, uv);
}

// Because w is used to store unrelated data, we split it out and store it
//  and then restore it after doing a matrix multiply.
// We take a w-value to attach to the position/velocity so that it is properly
//  handled as a position or vector.
float4 mul3 (float4 oldValue, float4x4 mat, float w) {
    float4 temp = mul(float4(oldValue.xyz, 1), mat);
    float3 divided;
    // FIXME: Is this right?
    if (w != 0)
        divided = temp.xyz / temp.w;
    else
        divided = temp.xyz;
    return float4(divided, oldValue.w);
}

bool checkCategoryFilter (float type, float2 typeMinMax) {
    return (type >= typeMinMax.x) && (type <= typeMinMax.y);
}

#endif