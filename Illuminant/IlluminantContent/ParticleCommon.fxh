struct ParticleSystemSettings {
    float2 ColumnsAndRows;
}

Texture2D LifeTexture;
sampler LifeTextureSampler {
    Texture = (LifeTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
}

Texture2D PositionTexture;
sampler PositionTextureSampler {
    Texture = (PositionTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
}

Texture2D VelocityTexture;
sampler VelocityTextureSampler {
    Texture = (VelocityTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
}