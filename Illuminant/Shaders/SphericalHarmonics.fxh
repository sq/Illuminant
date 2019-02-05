static const int SHValueCount = 9;
static const int SHTexelCount = SHValueCount;
static const float Pi = 3.141592654f;
static const float CosineA0 = Pi;
static const float CosineA1 = (2.0f * Pi) / 3.0f;
static const float CosineA2 = Pi * 0.25f;

struct SH9 {
    float3 a, b, c;
};

struct SH9Color {
    float3 a, b, c, d, e, f, g, h, i;
};

SH9 SHCosineLobe (in float3 dir) {
    SH9 sh;

    // Band 0
    sh.a.x = 0.282095f;

    // Band 1
    sh.a.y = 0.488603f * dir.y;
    sh.a.z = 0.488603f * dir.z;
    sh.b.x = 0.488603f * dir.x;

    // Band 2
    sh.b.y = 1.092548f * dir.x * dir.y;
    sh.b.z = 1.092548f * dir.y * dir.z;
    sh.c.x = 0.315392f * (3.0f * dir.z * dir.z - 1.0f);
    sh.c.y = 1.092548f * dir.x * dir.z;
    sh.c.z = 0.546274f * (dir.x * dir.x - dir.y * dir.y);

    return sh;
}

void SHScaleByCosine (inout SH9 r) {
    r.a.x *= CosineA0;

    r.a.y *= CosineA1;
    r.a.z *= CosineA1;
    r.b.x *= CosineA1;

    r.b.y *= CosineA2;
    r.b.z *= CosineA2;
    r.c *= CosineA2;
}

void SH9CAdd9 (inout SH9Color result, in SH9 operand, in float3 scale) {
    result.a += (operand.a.x * scale);
    result.b += (operand.a.y * scale);
    result.c += (operand.a.z * scale);
    result.d += (operand.b.x * scale);
    result.e += (operand.b.y * scale);
    result.f += (operand.b.z * scale);
    result.g += (operand.c.x * scale);
    result.h += (operand.c.y * scale);
    result.i += (operand.c.z * scale);
}

float3 SH9CSum9 (inout SH9Color sh9c, in SH9 operand) {
    float3 result = (sh9c.a * operand.a.x);
    result += (sh9c.b * operand.a.y);
    result += (sh9c.c * operand.a.z);
    result += (sh9c.d * operand.b.x);
    result += (sh9c.e * operand.b.y);
    result += (sh9c.f * operand.b.z);
    result += (sh9c.g * operand.c.x);
    result += (sh9c.h * operand.c.y);
    result += (sh9c.i * operand.c.z);
    return result;
}

void SHScaleColorByCosine (inout SH9Color r, float divisor) {
    float a0 = CosineA0 / divisor;
    float a1 = CosineA1 / divisor;
    float a2 = CosineA2 / divisor;

    r.a *= a0;

    r.b *= a1;
    r.c *= a1;
    r.d *= a1;

    r.e *= a2;
    r.f *= a2;
    r.g *= a2;
    r.h *= a2;
    r.i *= a2;
}