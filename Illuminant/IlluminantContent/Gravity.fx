#include "ParticleCommon.fxh"

#define MAX_ATTRACTORS 16

uniform int    AttractorCount;
uniform float3 AttractorPositions[MAX_ATTRACTORS];
uniform float2 AttractorRadiusesAndStrengths[MAX_ATTRACTORS];
uniform float3 MaximumVelocity;

void PS_Gravity (
    in  float2 xy                : VPOS,
    out float4 newPosition       : COLOR0,
    out float4 newVelocity       : COLOR1,
    out float4 newAttributes     : COLOR2
) {
    float4 oldVelocity;
    readState(
        xy * Texel, newPosition, oldVelocity, newAttributes
    );

    float3 acceleration = 0;

    for (int i = 0; i < AttractorCount; i++) {
        float3 apos = AttractorPositions[i];
        float2 ars = AttractorRadiusesAndStrengths[i];
        float3 toCenter = (apos - newPosition.xyz);
        float  distanceSquared = max(dot(toCenter, toCenter), ars.x);
        float  attraction = 1 / distanceSquared;
        acceleration += normalize(toCenter) * attraction * ars.y;
    }

    newVelocity = float4(
        min(MaximumVelocity, oldVelocity + acceleration), oldVelocity.w
    );
}

technique Gravity {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Gravity();
    }
}