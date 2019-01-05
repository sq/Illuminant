#define PI 3.14159265358979323846
#define BEZIERS_DEFINED

struct ClampedBezier1 {
    float4 RangeAndCount;
    float4 ABCD;
};

struct ClampedBezier2 {
    float4 RangeAndCount;
    float4 AB, CD;
};

struct ClampedBezier4 {
    float4 RangeAndCount;
    float4 A, B, C, D;
};

float tForScaledBezier (in float4 rangeAndCount, in float value, out float t) {
    float minValue = rangeAndCount.x, 
        invDivisor = rangeAndCount.y;
    uint mode = (uint)abs(rangeAndCount.w);
    bool repeating = mode > 255, bouncing = mode > 511;

    t = (value - minValue) * abs(invDivisor);

    if (bouncing) {
        t *= 2;

        if (invDivisor < 0)
            t = 2 - (t % 2);
        else
            t = t % 2;

        if (t > 1)
            t = 1 - (t - 1);
    } else if (repeating) {
        if (invDivisor < 0)
            t = 1 - (t % 1);
        else
            t = t % 1;
    } else {
        if (invDivisor < 0)
            t = 1 - saturate(t);
        else
            t = saturate(t);
    }

    switch (mode % 256) {
        default:
            break;
        case 1:
            t = sin(t * PI * 0.5);
            break;
        case 2:
            t = t * t;
            break;
    }

    return rangeAndCount.z;
}

float evaluateBezier1AtT (in ClampedBezier1 bezier, in float count, in float t) {
    float a = bezier.ABCD.x,
        b = bezier.ABCD.y,
        c = bezier.ABCD.z,
        d = bezier.ABCD.w;

    if (count <= 1.5)
        return a;

    float ab = lerp(a, b, t);
    if (count <= 2.5)
        return ab;

    float bc = lerp(b, c, t);
    float abbc = lerp(ab, bc, t);
    if (count <= 3.5)
        return abbc;

    float cd = lerp(c, d, t);
    float bccd = lerp(bc, cd, t);

    float result = lerp(abbc, bccd, t);
    return result;
}

float evaluateBezier1 (in ClampedBezier1 bezier, float value) {
    float t;
    float count = tForScaledBezier(bezier.RangeAndCount, value, t);
    return evaluateBezier1AtT(bezier, count, t);
}

float2 evaluateBezier2AtT (in ClampedBezier2 bezier, in float count, in float t) {
    float2 a = bezier.AB.xy,
        b = bezier.AB.zw,
        c = bezier.CD.xy,
        d = bezier.CD.zw;

    if (count <= 1.5)
        return a;

    float2 ab = lerp(a, b, t);
    if (count <= 2.5)
        return ab;

    float2 bc = lerp(b, c, t);
    float2 abbc = lerp(ab, bc, t);
    if (count <= 3.5)
        return abbc;

    float2 cd = lerp(c, d, t);
    float2 bccd = lerp(bc, cd, t);

    float2 result = lerp(abbc, bccd, t);
    return result;
}

float2 evaluateBezier2 (in ClampedBezier2 bezier, float value) {
    float t;
    float count = tForScaledBezier(bezier.RangeAndCount, value, t);
    return evaluateBezier2AtT(bezier, count, t);
}

float4 evaluateBezier4AtT (in ClampedBezier4 bezier, in float count, in float t) {
    float4 a = bezier.A,
        b = bezier.B,
        c = bezier.C,
        d = bezier.D;

    if (count <= 1.5)
        return a;

    float4 ab = lerp(a, b, t);
    if (count <= 2.5)
        return ab;

    float4 bc = lerp(b, c, t);
    float4 abbc = lerp(ab, bc, t);
    if (count <= 3.5)
        return abbc;

    float4 cd = lerp(c, d, t);
    float4 bccd = lerp(bc, cd, t);

    float4 result = lerp(abbc, bccd, t);
    return result;
}

float4 evaluateBezier4 (in ClampedBezier4 bezier, float value) {
    float t;
    float count = tForScaledBezier(bezier.RangeAndCount, value, t);
    return evaluateBezier4AtT(bezier, count, t);
}