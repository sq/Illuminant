#include "DistanceFunctionCommon.fxh"
#include "ParticleCommon.fxh"

uniform int    AreaType;
uniform float3 AreaCenter, AreaSize;
uniform float  AreaFalloff, AreaRotation;

uniform float2 CategoryFilter;

void PS_CollectParticles (
    ACCEPTS_VPOS,
    out float4 color : COLOR0
) {
    float2 xy = GET_VPOS;

    float4 uv = float4(xy * getTexel(), 0, 0);
    float4 worldPosition, velocity;
    readStatePV(
        xy, worldPosition, velocity
    );

    if (!checkCategoryFilter(velocity.w, CategoryFilter)) {
        discard;
        return;
    }

    float distance = evaluateByTypeId(
        AreaType, worldPosition, AreaCenter, AreaSize, AreaRotation
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
