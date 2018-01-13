#include "ParticleCommon.fxh"

uniform float LifeDecayRate;
uniform float MaximumVelocity;

void PS_Update (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition, oldVelocity;
    readState(
        xy * Texel, oldPosition, oldVelocity, newAttributes
    );

    // To support occlusion queries and reduce bandwidth used by dead particles
    if (oldPosition.w <= 0)
        discard;

    float3 velocity = oldVelocity.xyz;
    if (length(velocity) > MaximumVelocity)
        velocity = normalize(velocity) * MaximumVelocity;

    newPosition = float4(oldPosition.xyz + velocity, oldPosition.w - LifeDecayRate);
    newVelocity = float4(velocity, oldVelocity.w);
}

technique UpdatePositions {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Update();
    }
}
