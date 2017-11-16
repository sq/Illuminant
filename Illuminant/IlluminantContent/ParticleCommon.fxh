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

void VS_Update (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0,
    out float2 _xy    : POSITION1
) {
    result = float4((xy.x * 2) - 1, (xy.y * -2) + 1, 0, 1);
    _xy = xy;
}