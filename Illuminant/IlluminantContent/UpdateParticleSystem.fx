#include "ParticleCommon.fxh"

void vs (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0,
    out float2 _xy    : POSITION1
) {
    result = float4((xy.x * 2) - 1, (xy.y * -2) + 1, 0, 1);
    _xy = xy;
}

void ps (
    in  float2 xy          : POSITION1,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1
) {
    float4 oldPosition = tex2Dlod(PositionSampler, float4(xy + HalfTexel, 0, 0));
    float4 oldVelocity = tex2Dlod(VelocitySampler, float4(xy + HalfTexel, 0, 0));

    newPosition = float4(oldPosition.xyz + oldVelocity.xyz, oldPosition.w);
    newVelocity = oldVelocity;
}

technique UpdatePositions {
    pass P0
    {
        vertexShader = compile vs_3_0 vs();
        pixelShader = compile ps_3_0 ps();
    }
}
