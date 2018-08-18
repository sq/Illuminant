#include "ParticleCommon.fxh"
#include "DistanceFieldCommon.fxh"

#define SAMPLE sampleDistanceField
#define TVARS  DistanceFieldConstants
#define TRACE_MIN_STEP_SIZE 2
#define TRACE_FINAL_MIN_STEP_SIZE 12

#include "VisualizeCommon.fxh"

#define MAX_STEPS 3

uniform float EscapeVelocity;
uniform float BounceVelocityMultiplier;
uniform float LifeDecayRate;
uniform float MaximumVelocity;
uniform float CollisionDistance;
uniform float CollisionLifePenalty;

void PS_Update (
    in  float2 xy            : VPOS,
    out float4 newPosition   : COLOR0,
    out float4 newVelocity   : COLOR1,
    out float4 newAttributes : COLOR2
) {
    float4 oldPosition, oldVelocity;
    readStateOrDiscard(
        xy * Texel, oldPosition, oldVelocity, newAttributes
    );

    float3 velocity = oldVelocity.xyz;
    if (length(velocity) > MaximumVelocity)
        velocity = normalize(velocity) * MaximumVelocity;

    TVARS vars = makeDistanceFieldConstants();

    float oldDistance = SAMPLE(oldPosition.xyz, vars);
    float3 unitVector = normalize(velocity.xyz);
    float stepSpeed = length(velocity.xyz);

    bool collided = false;

    [loop]
    for (int i = 0; i < MAX_STEPS; i++) {
        float3 stepVelocity = unitVector * stepSpeed;
        float3 stepPosition = oldPosition.xyz + stepVelocity;
        float stepDistance = SAMPLE(stepPosition, vars);

        // No collision
        if (stepDistance > CollisionDistance)
            break;

        if (oldDistance < CollisionDistance) {
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
        if (length(normal) < 0.33)
            // HACK to avoid getting stuck at the center of volumes
            normal = float3(0, -1, 0);

        if (oldDistance > CollisionDistance) {
            // We started outside. Bounce away next step if configured to. Otherwise, we'll halt.
            float3 bounceVector = normalize(-(2 * dot(normal, unitVector) * (normal - unitVector)));
            newVelocity = float4(bounceVector * (min(MaximumVelocity, length(velocity.xyz) * BounceVelocityMultiplier)), oldVelocity.w);
            // Deduct the collision life penalty because we just entered a collision state.
            newPosition.w -= CollisionLifePenalty;
        } else {
            // We started inside, so flee at our escape velocity.
            float3 escapeVector = normalize(normal);
            newVelocity = float4(escapeVector * EscapeVelocity, oldVelocity.w);
        }
    } else {
        newPosition = float4(oldPosition.xyz + velocity.xyz, oldPosition.w - LifeDecayRate);
        newVelocity = float4(velocity, oldVelocity.w);
    }
}

technique UpdateWithDistanceField {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Update();
    }
}
