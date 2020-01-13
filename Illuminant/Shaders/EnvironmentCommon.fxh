#ifndef ENVIRONMENT_DEFINED
#define ENVIRONMENT_DEFINED

uniform float4 EnvironmentZAndScale;
uniform float4 EnvironmentZToY;

float getGroundZ () {
    return EnvironmentZAndScale.x;
}

float getMaximumZ () {
    return EnvironmentZAndScale.y;
}

float getZToYMultiplier () {
    return EnvironmentZToY.x;
}

float getInvZToYMultiplier () {
    return EnvironmentZToY.y;
}

float2 getEnvironmentRenderScale () {
    return EnvironmentZAndScale.zw;
}

#endif