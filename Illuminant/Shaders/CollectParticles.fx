#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform int    AreaType;
uniform float3 AreaCenter, AreaSize;
uniform float  AreaFalloff;

void PS_CollectParticles (
    in  float2 xy    : VPOS,
    out float4 color : COLOR0
) {
    float4 uv = float4(xy * getTexel(), 0, 0);
    float4 worldPosition = tex2Dlod(PositionSampler, uv);

    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize
    );
    float scaledDistance = (1 - saturate(distance / AreaFalloff));

    if ((worldPosition.w <= 1) || (scaledDistance <= 0.01)) {
        color = 0;
        discard;
    } else {
        color = worldPosition.w;
    }
}

technique CollectParticles {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_CollectParticles();
    }
}
