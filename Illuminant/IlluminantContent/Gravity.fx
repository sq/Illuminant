#include "ParticleCommon.fxh"

uniform float3 Position;
uniform float  RadiusSquared;
uniform float  Strength;

void PS_Gravity (
    in  float2 xy          : POSITION1,
    out float4 newPosition : COLOR0,
    out float4 newVelocity : COLOR1
) {
    float4 oldPosition = tex2Dlod(PositionSampler, float4(xy + HalfTexel, 0, 0));
    float4 oldVelocity = tex2Dlod(VelocitySampler, float4(xy + HalfTexel, 0, 0));

    float3 toCenter = (Position - oldPosition.xyz);
    float  distanceSquared = max(dot(toCenter, toCenter), RadiusSquared);
    float  attraction = Strength / distanceSquared;
    float3 acceleration = normalize(toCenter) * attraction;

    newPosition = oldPosition;
    newVelocity = float4(
        oldVelocity + acceleration, oldVelocity.w
    );
}

technique Gravity {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Gravity();
    }
}