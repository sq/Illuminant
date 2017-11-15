uniform float2 HalfTexel;

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