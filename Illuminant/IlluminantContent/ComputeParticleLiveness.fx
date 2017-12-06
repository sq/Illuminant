#include "ParticleCommon.fxh"

float sampleLiveness (float2 xy) {
    float4 position = tex2Dlod(PositionSampler, float4(xy, 0, 0));
    return (position.w > 0) ? 1 : 0;
}

void PS_ComputeLiveness (
    in  float2 xy     : VPOS,
    out float4 packed : COLOR0
) {
    xy *= Texel;
    xy.x *= 4;
    float2 texelX = float2(Texel.x, 0);
    float a = sampleLiveness(xy);
    xy += texelX;
    float b = sampleLiveness(xy);
    xy += texelX;
    float c = sampleLiveness(xy);
    xy += texelX;
    float d = sampleLiveness(xy);

    float sum = (a) + (b * 4) + (c * 16) + (d * 64);

    packed = sum / 256;
}

technique ComputeLiveness {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_ComputeLiveness();
    }
}
