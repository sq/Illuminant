#include "ParticleCommon.fxh"

void PS_Update (
    in  float2 xy            : POSITION1,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition, oldVelocity;
    readState(
        xy, oldPosition, oldVelocity, newAttributes
    );

    newPosition = float4(oldPosition.xyz + oldVelocity.xyz, oldPosition.w);
    newVelocity = oldVelocity;
}

technique UpdatePositions {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Update();
    }
}
