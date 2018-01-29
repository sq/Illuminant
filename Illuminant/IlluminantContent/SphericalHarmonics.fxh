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

    sh.c[0] = 0.282095f * CosineA0;

    sh.c[1] = 0.488603f * dir.y * CosineA1;
    sh.c[2] = 0.488603f * dir.z * CosineA1;
    sh.c[3] = 0.488603f * dir.x * CosineA1;

    sh.c[4] = 1.092548f * dir.x * dir.y * CosineA2;
    sh.c[5] = 1.092548f * dir.y * dir.z * CosineA2;
    sh.c[6] = 0.315392f * (3.0f * dir.z * dir.z - 1.0f) * CosineA2;
    sh.c[7] = 1.092548f * dir.x * dir.z * CosineA2;
    sh.c[8] = 0.546274f * (dir.x * dir.x - dir.y * dir.y) * CosineA2;

    return sh;
}