#ifndef RAMP_DEFINED
#define RAMP_DEFINED

Texture2D RampTexture        : register(t3);
sampler   RampTextureSampler : register(s3) {
    Texture   = (RampTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
};

// FIXME: In FNA ramp textures + sphere lights creates very jagged shadows but in XNA it does not.
float SampleFromRamp (float value) {
    return tex2Dlod(RampTextureSampler, float4(value, 0, 0, 0)).r;
}

#endif