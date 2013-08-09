
Texture2D RampTexture : register(t1);

sampler RampTextureSampler : register(s1) {
    Texture = (RampTexture);
};

float RampLookup (float value) {
    return tex2D(RampTextureSampler, float2(value, 0)).r;
}