static const int SHValueCount = 9;
static const int SHTexelCount = SHValueCount;

static const float Pi = 3.141592654f;
static const float CosineA0 = Pi;
static const float CosineA1 = (2.0f * Pi) / 3.0f;
static const float CosineA2 = Pi * 0.25f;

void SHCosineLobe (
    in float3 dir, 
    out float a, out float b, out float c, 
    out float d, out float e, out float f, 
    out float g, out float h, out float i
) {
    // Band 0
    a = 0.282095f * CosineA0;

    // Band 1
    b = 0.488603f * dir.y * CosineA1;
    c = 0.488603f * dir.z * CosineA1;
    d = 0.488603f * dir.x * CosineA1;

    // Band 2
    e = 1.092548f * dir.x * dir.y * CosineA2;
    f = 1.092548f * dir.y * dir.z * CosineA2;
    g = 0.315392f * (3.0f * dir.z * dir.z - 1.0f) * CosineA2;
    h = 1.092548f * dir.x * dir.z * CosineA2;
    i = 0.546274f * (dir.x * dir.x - dir.y * dir.y) * CosineA2;
}

/*
float3 ComputeSHIrradiance (in float3 normal, in SH9Color radiance) {
    // Compute the cosine lobe in SH, oriented about the normal direction
    float a, b, c, d, e, f, g, h, i;
    SHCosineLobe(normal);

    // Compute the SH dot product to get irradiance
    float3 irradiance = 0.0f;
    for(uint i = 0; i < 9; ++i)
        irradiance += radiance.c[i] * shCosine.c[i];

    return irradiance;
}

float3 ComputeSHDiffuse (in float3 normal, in SH9Color radiance, in float3 diffuseAlbedo) {
    return ComputeSHIrradiance(normal, radiance) * diffuseAlbedo * (1.0f / Pi);
}
*/