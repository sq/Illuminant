#include "ParticleCommon.fxh"

#define MAX_ATTRACTORS 16

uniform int    AttractorCount;
uniform float3 AttractorPositions[MAX_ATTRACTORS];
uniform float2 AttractorRadiusesAndStrengths[MAX_ATTRACTORS];

void PS_Gravity (
    in  float2 xy                : POSITION1,
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
        float3 toCenter = (AttractorPositions[i] - newPosition.xyz);
        float  distanceSquared = max(dot(toCenter, toCenter), AttractorRadiusesAndStrengths[i].x);
        float  attraction = abs(AttractorRadiusesAndStrengths[i].y) / distanceSquared;
        if (AttractorRadiusesAndStrengths[i].y < 0)
            attraction = -attraction;
        acceleration += normalize(toCenter) * attraction;
    }

    newVelocity = float4(
        oldVelocity + acceleration, oldVelocity.w
    );
}

technique Gravity {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Gravity();
    }
}