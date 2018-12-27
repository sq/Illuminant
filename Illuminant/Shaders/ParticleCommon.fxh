struct ParticleSystemSettings {
    // deltaTimeSeconds, friction, maximumVelocity, lifeDecayRate
    float4 GlobalSettings;
    // escapeVelocity, bounceVelocityMultiplier, collisionDistance, collisionLifePenalty
    float4 CollisionSettings;
    float4 TexelAndSize;
    float2 RotationFromLifeAndIndex;
};

uniform ParticleSystemSettings System;
uniform float StippleFactor;

float getDeltaTimeSeconds () {
    return System.GlobalSettings.x / 1000;
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

#ifdef BEZIERS_DEFINED

uniform ClampedBezier2 SizeFromLife;
uniform ClampedBezier4 ColorFromLife;

float4 getColorForLifeValue (float life) {
    float4 result = evaluateBezier4(ColorFromLife, life);
    result.rgb *= result.a;
    return result;
}

float2 getSizeForLifeValue (float life) {
    float2 result = evaluateBezier2(SizeFromLife, life);
    return System.TexelAndSize.zw * result;
}

#endif

float getRotationForLifeAndIndex (float life, float index) {
    return (life * System.RotationFromLifeAndIndex.x) + 
        (index * System.RotationFromLifeAndIndex.y);
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

static const float thresholdMatrix[] = {
    1.0 / 17.0,  9.0 / 17.0,  3.0 / 17.0, 11.0 / 17.0,
    13.0 / 17.0,  5.0 / 17.0, 15.0 / 17.0,  7.0 / 17.0,
    4.0 / 17.0, 12.0 / 17.0,  2.0 / 17.0, 10.0 / 17.0,
    16.0 / 17.0,  8.0 / 17.0, 14.0 / 17.0,  6.0 / 17.0
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
    position = tex2Dlod(PositionSampler, uv);
    velocity = tex2Dlod(VelocitySampler, uv);
    attributes = tex2Dlod(AttributeSampler, uv);
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
    if (position.w <= 0)
        discard;

    velocity = tex2Dlod(VelocitySampler, uv);
    attributes = tex2Dlod(AttributeSampler, uv);
}

bool stippleReject (float vertexIndex) {
    return false;
    float stippleThreshold = thresholdMatrix[vertexIndex % 16];
    return (StippleFactor - stippleThreshold) <= 0;
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
