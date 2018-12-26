#include "ParticleCommon.fxh"

static const float3 Corners[] = {
    { -1, -1, 0 },
    { 1, -1, 0 },
    { 1, 1, 0 },
    { -1, 1, 0 }
};

void VS_CountLiveParticles (
    in  float2 xy             : POSITION0,
    in  float3 offsetAndIndex : POSITION1,
    in  int2   cornerIndex    : BLENDINDICES0, // 0-3
    out float4 result : POSITION0,
    out float4 position : TEXCOORD1
) {
    float4 actualXy = float4(xy + offsetAndIndex.xy, 0, 0);
    position = tex2Dlod(PositionSampler, actualXy);
    float scale;
    if (position.w > 1)
        scale = 1;
    else
        scale = 0;

    result = float4(Corners[cornerIndex.x].xy * scale * 2, 0, scale);
}

void PS_CountLiveParticles (
    in  float4 position : TEXCOORD1,
    out float4 color    : COLOR0
) {
    color = position.w;
    if (position.w <= 1)
        discard;
}

technique CountLiveParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_CountLiveParticles();
        pixelShader = compile ps_3_0 PS_CountLiveParticles();
    }
}
