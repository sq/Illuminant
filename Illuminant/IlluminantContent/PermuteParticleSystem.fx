#include "ParticleCommon.fxh"

uniform float3 Add, Multiply;

void PS_PositionFMA (
    in  float2 xy          : POSITION1,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1
) {
    float4 oldPosition = tex2Dlod(PositionSampler, float4(xy + HalfTexel, 0, 0));
    float4 oldVelocity = tex2Dlod(VelocitySampler, float4(xy + HalfTexel, 0, 0));

    newPosition = float4((oldPosition.xyz * Multiply) + Add, oldPosition.w);
    newVelocity = oldVelocity;
}

void PS_VelocityFMA(
    in  float2 xy          : POSITION1,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1
) {
    float4 oldPosition = tex2Dlod(PositionSampler, float4(xy + HalfTexel, 0, 0));
    float4 oldVelocity = tex2Dlod(VelocitySampler, float4(xy + HalfTexel, 0, 0));

    newPosition = oldPosition;
    newVelocity = float4((oldVelocity.xyz * Multiply) + Add, oldVelocity.w);
}

technique PositionFMA {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_PositionFMA();
    }
}

technique VelocityFMA {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_VelocityFMA();
    }
}
