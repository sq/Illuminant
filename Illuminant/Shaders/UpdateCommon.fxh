#ifndef UPDATE_DEFINED
#define UPDATE_DEFINED

Texture2D LifeRampTexture;
sampler LifeRampSampler {
    Texture = (LifeRampTexture);
    AddressU = CLAMP;
    AddressV = WRAP;
    MinFilter = POINT;
    MagFilter = POINT;
};

// ramp_strength, ramp_min, ramp_divisor, index_divisor
uniform float4 LifeRampSettings;

float3 applyFrictionAndMaximum (float3 velocity) {
    float l = length(velocity);
    if (l > getMaximumVelocity())
        l = getMaximumVelocity();

    float friction = l * getFriction();

    l -= (friction * getDeltaTimeSeconds());
    l = clamp(l, 0, getMaximumVelocity());

    return normalize(velocity) * l;
}

float4 readLifeRamp (float u, float v) {
    return tex2Dlod(LifeRampSampler, float4(u, v, 0, 0));
}

#ifdef INCLUDE_RAMPS

uniform ClampedBezier1 SizeFromLife;
uniform ClampedBezier1 SizeFromVelocity;
uniform ClampedBezier4 ColorFromLife;
uniform ClampedBezier4 ColorFromVelocity;

float4 getColorForLifeAndVelocity (float life, float velocityLength) {
    float4 result = evaluateBezier4(ColorFromLife, life);
    result *= evaluateBezier4(ColorFromVelocity, velocityLength);
    return result;
}

float getSizeForLifeAndVelocity (float life, float velocityLength) {
    float result = evaluateBezier1(SizeFromLife, life);
    result *= evaluateBezier1(SizeFromVelocity, velocityLength);
    return result;
}

#endif

float4 getRampedColorForLifeValueAndIndex (float life, float velocityLength, float index) {
    float4 result = getColorForLifeAndVelocity(life, velocityLength);

    [branch]
    if (LifeRampSettings.x != 0) {
        float u = (life - LifeRampSettings.y) / LifeRampSettings.z;
        if (LifeRampSettings.x < 0)
            u = 1 - saturate(u);
        float v = index / LifeRampSettings.w;
        result = lerp(result, readLifeRamp(u, v) * result, saturate(abs(LifeRampSettings.x)));
    }

    return result;
}

float getRotationForVelocity (float3 velocity) {
    float2 absvel = abs(velocity.xy + float2(0, velocity.z * -getZToY()));

    float angle;
    if ((absvel.x < 0.01) && (absvel.y < 0.01))
        return 0;
    
    float result = atan2(velocity.y, velocity.x);
    if (result < 0)
        result += 2 * PI;
    return result;
}

void computeRenderData (
    in float2 vpos, 
    in float4 position, in float4 velocity, in float4 attributes, 
    out float4 renderColor, out float4 renderData
) {
    if (position.w <= 0) {
        renderColor = renderData = 0;
        return;
    }
    // FIXME
    float index = vpos.x + (vpos.y * 256);
    float velocityLength = max(length(velocity), 0.001);
    renderColor = attributes * getRampedColorForLifeValueAndIndex(position.w, velocityLength, index);
    renderColor.a = saturate(renderColor.a);
    renderColor.rgb *= renderColor.a;
    renderData.x = getSizeForLifeAndVelocity(position.w, velocityLength);
    renderData.y = getRotationForVelocity(velocity) +
        getRotationForLifeAndIndex(position.w, index);
    renderData.z = velocityLength;
    renderData.w = velocity.w;
}

#endif