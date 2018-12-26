static const int SHValueCount = 9;
static const int SHTexelCount = SHValueCount;
static const float Pi = 3.141592654f;
static const float CosineA0 = Pi;
static const float CosineA1 = (2.0f * Pi) / 3.0f;
static const float CosineA2 = Pi * 0.25f;

struct SH9 {
    float c[9];
};

struct SH9Color {
    float3 c[9];
};

SH9 SHCosineLobe (in float3 dir) {
    SH9 sh;

    // Band 0
    sh.c[0] = 0.282095f;

    // Band 1
    sh.c[1] = 0.488603f * dir.y;
    sh.c[2] = 0.488603f * dir.z;
    sh.c[3] = 0.488603f * dir.x;

    // Band 2
    sh.c[4] = 1.092548f * dir.x * dir.y;
    sh.c[5] = 1.092548f * dir.y * dir.z;
    sh.c[6] = 0.315392f * (3.0f * dir.z * dir.z - 1.0f);
    sh.c[7] = 1.092548f * dir.x * dir.z;
    sh.c[8] = 0.546274f * (dir.x * dir.x - dir.y * dir.y);

    return sh;
}

void SHScaleByCosine (inout SH9 r) {
    r.c[0] *= CosineA0;

    r.c[1] *= CosineA1;
    r.c[2] *= CosineA1;
    r.c[3] *= CosineA1;

    r.c[4] *= CosineA2;
    r.c[5] *= CosineA2;
    r.c[6] *= CosineA2;
    r.c[7] *= CosineA2;
    r.c[8] *= CosineA2;
}

void SHScaleColorByCosine (inout SH9Color r, float divisor) {
    float a0 = CosineA0 / divisor;
    float a1 = CosineA1 / divisor;
    float a2 = CosineA2 / divisor;

    r.c[0] *= a0;

    r.c[1] *= a1;
    r.c[2] *= a1;
    r.c[3] *= a1;

    r.c[4] *= a2;
    r.c[5] *= a2;
    r.c[6] *= a2;
    r.c[7] *= a2;
    r.c[8] *= a2;
}