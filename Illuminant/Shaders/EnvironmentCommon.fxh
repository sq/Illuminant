#ifndef ENVIRONMENT_DEFINED
#define ENVIRONMENT_DEFINED

struct EnvironmentSettings {
    float4 ZAndScale;
};

uniform EnvironmentSettings Environment;

float getGroundZ () {
    return Environment.ZAndScale.x;
}

float getZToYMultiplier () {
    return Environment.ZAndScale.y;
}

float getInvZToYMultiplier () {
    return 1.0 / Environment.ZAndScale.y;
}

float getEnvironmentRenderScale () {
    return Environment.ZAndScale.zw;
}

#endif