#include "ParticleCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants

#include "VisualizeCommon.fxh"

#define MAX_STEPS 3

uniform float EscapeVelocity;
uniform float BounceVelocityMultiplier;
uniform float LifeDecayRate;

void PS_Update (
    in  float2 xy            : POSITION1,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition, oldVelocity;
    readState(
        xy, oldPosition, oldVelocity, newAttributes
    );

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float oldDistance = sampleDistanceField(oldPosition.xyz, vars);
    float3 unitVector = normalize(oldVelocity.xyz);
    float stepSpeed = length(oldVelocity.xyz);

    bool collided = false;

    [loop]
    for (int i = 0; i < MAX_STEPS; i++) {
        float3 stepVelocity = unitVector * stepSpeed;
        float3 stepPosition = oldPosition.xyz + stepVelocity;
        float stepDistance = sampleDistanceField(stepPosition, vars);

        // No collision
        if (stepDistance > 0)
            break;

        if (oldDistance < 0) {
            collided = true;
            break;
        } else {
            collided = true;

            // Moving outside of all volumes but our path is blocked
            stepSpeed = min(stepSpeed, abs(stepDistance) - 0.5);
        }
    }

    [branch]
    if (collided) {
        newPosition = float4(oldPosition + (unitVector * stepSpeed), oldPosition.w - LifeDecayRate);

        float3 normal = estimateNormal(newPosition.xyz, vars);
        if (length(normal) < 0.01)
            // HACK to avoid getting stuck at the center of volumes
            normal = float3(0, -1, 0);

        if (oldDistance > 0) {
            // We started outside. Bounce away next step if configured to. Otherwise, we'll halt.
            float3 bounceVector = normalize(-(2 * dot(normal, unitVector) * (normal - unitVector)));
            newVelocity = float4(bounceVector * (length(oldVelocity.xyz) * BounceVelocityMultiplier), oldVelocity.w);
        } else {
            // We started inside, so flee at our escape velocity.
            newVelocity = float4(normal * min(EscapeVelocity, abs(oldDistance)), oldVelocity.w);
        }
    } else {
        newPosition = float4(oldPosition.xyz + oldVelocity.xyz, oldPosition.w - LifeDecayRate);
        newVelocity = oldVelocity;
    }
}

technique UpdateWithDistanceField {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Update();
    }
}
