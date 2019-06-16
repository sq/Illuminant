#include "ParticleCommon.fxh"

static const float3 Corners[] = {
    { 0, 0, 0 },
    { 1, 0, 0 },
    { 1, 1, 0 },
    { 0, 1, 0 }
};

uniform float2 ChunkIndexAndMaxIndex;

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

    float xPx = ChunkIndexAndMaxIndex.x;
    float widthPx = ChunkIndexAndMaxIndex.y;
    float scaledX = xPx / widthPx;
    float2 scaledCorner = Corners[cornerIndex.x].xy;
    scaledCorner.x /= widthPx;

    result = float4(scaledCorner * scale, 0, scale);
}

void PS_CountLiveParticles (
    in  float4 position : TEXCOORD1,
    out float4 color    : COLOR0
) {
    if (position.w <= 1) {
        //discard;
        color = 0;
    } else {
        color = 1.0 / 1024.0;
    }
}

technique CountLiveParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_CountLiveParticles();
        pixelShader = compile ps_3_0 PS_CountLiveParticles();
    }
}
