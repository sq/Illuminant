#include "ParticleCommon.fxh"

#define MAX_ATTRACTORS 16

uniform int    AttractorCount;
uniform float3 AttractorPositions[MAX_ATTRACTORS];
uniform float3 AttractorRadiusesAndStrengths[MAX_ATTRACTORS];
uniform float  MaximumAcceleration;

void PS_Gravity (
    in  float2 xy                : VPOS,
    out float4 newPosition       : COLOR0,
    out float4 newVelocity       : COLOR1,
    out float4 newAttributes     : COLOR2
) {
    float4 oldVelocity;
    readState(
        xy, newPosition, oldVelocity, newAttributes
    );

    float3 acceleration = 0;

    for (int i = 0; i < AttractorCount; i++) {
        float3 apos = AttractorPositions[i];
        float3 ars = AttractorRadiusesAndStrengths[i];
        float3 toCenter = (apos - newPosition.xyz);
        float  attraction = 0;
        if (ars.z >= 0.5) {
            float distance = length(toCenter);
            attraction = 1 - saturate(distance / ars.x);
            attraction = attraction * getDeltaTime() / 1000;
        } else {
            float  distanceSquared = dot(toCenter, toCenter) - ars.x;
            distanceSquared = max(distanceSquared, 0.001);
            // FIXME: For some reason time-scaling doesn't work for this version of the formula
            attraction = 1 / distanceSquared;
        }
        float3 newAccel = normalize(toCenter) * attraction * ars.y;
        acceleration += newAccel;
    }

    float maximumAcceleration = MaximumAcceleration * getDeltaTime() / 1000;

    float currentLength = length(acceleration);
    if (currentLength > maximumAcceleration)
        acceleration = normalize(acceleration) * maximumAcceleration;

    newVelocity = float4(
        min(getMaximumVelocity(), oldVelocity + acceleration), oldVelocity.w
    );
}

technique Gravity {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Gravity();
    }
}