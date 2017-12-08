#include "ParticleCommon.fxh"

float sampleLiveness (float2 xy) {
    float4 position = tex2Dlod(PositionSampler, float4(xy, 0, 0));
    return (position.w > 0) ? 1 : 0;
}

void PS_ComputeLiveness (
    in  float2 xy     : VPOS,
    out float4 packed : COLOR0
) {
    xy.x *= 8;
    xy *= Texel;
    float2 texelX = float2(Texel.x, 0);

    float sum = 0, multiplier = 1;
    while (multiplier <= 128) {
        float s = sampleLiveness(xy);
        xy += texelX;
        sum += (s * multiplier);
        multiplier *= 2;
    }

    packed = sum / 255;
}

technique ComputeLiveness {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_ComputeLiveness();
    }
}
