#ifndef ENVIRONMENT_DEFINED
#define ENVIRONMENT_DEFINED

struct EnvironmentSettings {
    float3 _Z;
    float2 RenderScale;
};

uniform EnvironmentSettings Environment;

float getGroundZ () {
    return Environment._Z.x;
}

float getZToYMultiplier () {
    return Environment._Z.y;
}

float getInvZToYMultiplier () {
    return Environment._Z.z;
}

#endif