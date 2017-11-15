#include "ParticleCommon.fxh"

void vs (
    in  float2 xy     : POSITION0,
    out float4 result : POSITION0,
    out float2 _xy    : POSITION1
) {
    result = float4(xy, 0, 1);
    _xy = xy;
}

void ps (
    in  float2 xy     : POSITION1,
    out float4 result : COLOR0
) {
    result = float4(xy, 0, 1);
}

technique RasterizeParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 vs();
        pixelShader = compile ps_3_0 ps();
    }
}
