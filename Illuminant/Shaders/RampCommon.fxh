#ifndef RAMP_DEFINED
#define RAMP_DEFINED

Texture2D RampTexture        : register(t3);
sampler   RampTextureSampler : register(s3) {
    Texture   = (RampTexture);
    AddressU  = CLAMP;
    AddressV  = WRAP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// FIXME: In FNA ramp textures + sphere lights creates very jagged shadows but in XNA it does not.
float SampleFromRamp (float x) {
    return tex2Dlod(RampTextureSampler, float4(x, 0, 0, 0)).r;
}

float SampleFromRamp2 (float2 xy) {
    return tex2Dlod(RampTextureSampler, float4(xy, 0, 0)).r;
}

#endif