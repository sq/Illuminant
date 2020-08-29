#pragma fxcparams(/O3 /Zi)

#define INCLUDE_RAMPS
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "Bezier.fxh"
#include "ParticleCommon.fxh"
#include "UpdateCommon.fxh"

void PS_Update (
    ACCEPTS_VPOS,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1,
    out float4 renderColor : COLOR2,
    out float4 renderData  : COLOR3
) {
    newPosition = newVelocity = renderColor = renderData = 0;

    float2 xy = GET_VPOS;
    float4 oldPosition, oldVelocity, attributes;
    readStateOrDiscard(
        xy, oldPosition, oldVelocity, attributes
    );

    float3 velocity = applyFrictionAndMaximum(oldVelocity.xyz);

    float3 scaledVelocity = velocity * getDeltaTimeSeconds();

    float newLife = oldPosition.w - (getLifeDecayRate() * getDeltaTimeSeconds());
    if (newLife <= 0) {
        newPosition = 0;
        newVelocity = 0;
    } else {
        newPosition = float4(oldPosition.xyz + scaledVelocity, newLife);
        newVelocity = float4(velocity, oldVelocity.w);
    }

    computeRenderData(xy, newPosition, newVelocity, attributes, renderColor, renderData);
}

void PS_Erase (
    ACCEPTS_VPOS,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1,
    out float4 renderColor : COLOR2,
    out float4 renderData  : COLOR3
) {
    float2 xy = GET_VPOS;
    newPosition = newVelocity = renderColor = renderData = 0;
}

technique UpdatePositions {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Update();
    }
}

technique Erase {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Erase();
    }
}
