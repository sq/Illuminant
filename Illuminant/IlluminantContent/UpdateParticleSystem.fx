#include "ParticleCommon.fxh"
#include "UpdateCommon.fxh"

uniform float LifeDecayRate;

void PS_Update (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition, oldVelocity;
    readStateOrDiscard(
        xy * Texel, oldPosition, oldVelocity, newAttributes
    );

    float3 velocity = applyFrictionAndMaximum(oldVelocity.xyz);

    float3 scaledVelocity = velocity * DeltaTimeSeconds;

    float newLife = oldPosition.w - (LifeDecayRate * DeltaTimeSeconds);
    if (newLife <= 0) {
        newPosition = 0;
        newVelocity = 0;
    } else {
        newPosition = float4(oldPosition.xyz + scaledVelocity, newLife);
        newVelocity = float4(velocity, oldVelocity.w);
    }
}

technique UpdatePositions {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Update();
    }
}
