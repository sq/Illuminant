
Texture2D RampTexture : register(t1);

sampler RampTextureSampler : register(s1) {
    Texture = (RampTexture);
};

float RampLookup (float value) {
    return tex2Dgrad(RampTextureSampler, float2(value, 0), 0, 0).r;
}