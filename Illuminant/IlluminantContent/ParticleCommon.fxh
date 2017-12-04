uniform float2 Texel;

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

void readState (
    in float2 xy,
    out float4 position,
    out float4 velocity,
    out float4 attributes
) {
    position = tex2Dlod(PositionSampler, float4(xy, 0, 0));
    velocity = tex2Dlod(VelocitySampler, float4(xy, 0, 0));
    attributes = tex2Dlod(AttributeSampler, float4(xy, 0, 0));
}