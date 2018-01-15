#include "ParticleCommon.fxh"

void VS_Null (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0
) {
    result = float4(xy.x, xy.y, 0, 1);
}

void PS_Null (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    readStateOrDiscard(
        xy * Texel, newPosition, newVelocity, newAttributes
    );
}

technique NullTransform {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Null();
        pixelShader = compile ps_3_0 PS_Null();
    }
}
