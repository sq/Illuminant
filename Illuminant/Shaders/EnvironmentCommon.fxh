#ifndef ENVIRONMENT_DEFINED
#define ENVIRONMENT_DEFINED

#define PI 3.14159265358979323846

uniform const float4 EnvironmentZAndScale;
uniform const float4 EnvironmentZToY;

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

// https://aras-p.info/texts/CompactNormalStorage.html#method03spherical
float2 encodeNormalSpherical (float3 n) {
    if (abs(n.x) < 0.0001)
        n.x = 0.0001;
    return float2(
      (float2(atan2(n.y,n.x) / PI, n.z) + 1.0) * 0.5
    );
}

float3 decodeNormalSpherical (float2 enc) {
    float2 ang = enc*2-1;
    float2 scth;
    sincos(ang.x * PI, scth.x, scth.y);
    float2 scphi = float2(sqrt(1.0 - ang.y*ang.y), ang.y);
    return float3(scth.y*scphi.x, scth.x*scphi.x, scphi.y);
}

#endif