uniform float2 RandomnessTexel;
uniform float2 RandomnessOffset;

Texture2D RandomnessTexture;
sampler RandomnessSampler {
    Texture   = (RandomnessTexture);
    AddressU  = WRAP;
    AddressV  = WRAP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

float4 random (float2 xy) {
    return tex2Dlod(RandomnessSampler, float4((xy + RandomnessOffset) * RandomnessTexel, 0, 0));
}