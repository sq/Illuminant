struct ParticleSystemSettings {
    float2 Texel;
    // deltaTimeSeconds, friction, maximumVelocity, lifeDecayRate
    float4 GlobalSettings;
    // escapeVelocity, bounceVelocityMultiplier, collisionDistance, collisionLifePenalty
    float4 CollisionSettings;
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
    float4 uv = float4(xy * System.Texel, 0, 0);
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
    float4 uv = float4(xy * System.Texel, 0, 0);
    position = tex2Dlod(PositionSampler, uv);

    // To support occlusion queries and reduce bandwidth used by dead particles
    if (position.w <= 0)
        discard;

    velocity = tex2Dlod(VelocitySampler, uv);
    attributes = tex2Dlod(AttributeSampler, uv);
}

bool stippleReject (float vertexIndex) {
    float stippleThreshold = thresholdMatrix[vertexIndex % 16];
    return (StippleFactor - stippleThreshold) <= 0;
}
