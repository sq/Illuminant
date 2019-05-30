#ifndef ENVIRONMENT_DEFINED
#define ENVIRONMENT_DEFINED

uniform float4 EnvironmentZAndScale;

float getGroundZ () {
    return EnvironmentZAndScale.x;
}

float getZToYMultiplier () {
    return EnvironmentZAndScale.y;
}

float getInvZToYMultiplier () {
    if (abs(EnvironmentZAndScale.y) > 0)
        return 1.0 / EnvironmentZAndScale.y;
    else
        return 0;
}

float getEnvironmentRenderScale () {
    return EnvironmentZAndScale.zw;
}

#endif