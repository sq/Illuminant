#ifndef RANDOM_DEFINED
#define RANDOM_DEFINED

uniform float2 RandomnessTexel;
uniform float2 RandomnessOffset;

Texture2D LowPrecisionRandomnessTexture;
sampler LowPrecisionRandomnessSampler {
    Texture   = (LowPrecisionRandomnessTexture);
    AddressU  = WRAP;
    AddressV  = WRAP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

Texture2D RandomnessTexture;
sampler RandomnessSampler {
    Texture   = (RandomnessTexture);
    AddressU  = WRAP;
    AddressV  = WRAP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

float4 randomCustom (float2 xy, float2 offset, float2 rate) {
    float4 randomUv = float4(((xy * rate) + offset) * RandomnessTexel, 0, 0);
    return tex2Dlod(RandomnessSampler, randomUv);
}

float4 random (float2 xy) {
    return randomCustom(xy, RandomnessOffset, RandomnessTexel);
}

float4 smoothRandomCustom (float2 xy, float2 offset, float2 rate) {
    float4 randomUv = float4(((xy * rate) + offset) * RandomnessTexel, 0, 0);
    return tex2Dlod(LowPrecisionRandomnessSampler, randomUv);
}

float4 smoothRandom (float2 xy) {
    return smoothRandomCustom(xy, RandomnessOffset, RandomnessTexel);
}

#endif