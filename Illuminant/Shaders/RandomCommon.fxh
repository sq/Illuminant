#ifndef RANDOM_DEFINED
#define RANDOM_DEFINED

uniform float2 RandomnessTexel;
uniform float2 RandomnessOffset;

#ifdef SMOOTH_NOISE
Texture2D LowPrecisionRandomnessTexture;
sampler RandomnessSampler {
    Texture   = (LowPrecisionRandomnessTexture);
    AddressU  = WRAP;
    AddressV  = WRAP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};
#else
Texture2D RandomnessTexture;
sampler RandomnessSampler {
    Texture   = (RandomnessTexture);
    AddressU  = WRAP;
    AddressV  = WRAP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};
#endif

float4 randomCustomRate (float2 xy, float2 rate) {
    float4 randomUv = float4((xy + RandomnessOffset) * rate, 0, 0);
    return tex2Dlod(RandomnessSampler, randomUv);
}

float4 random (float2 xy) {
    return randomCustomRate(xy, RandomnessTexel);
}

#endif