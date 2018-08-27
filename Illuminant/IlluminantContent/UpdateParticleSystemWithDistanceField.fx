#include "ParticleCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "UpdateCommon.fxh"

#define SAMPLE sampleDistanceFieldEx
#define TVARS  DistanceFieldConstants
#define TRACE_MIN_STEP_SIZE 2
#define TRACE_FINAL_MIN_STEP_SIZE 12
#define FALSE_BOUNCE_HACK 0.5
#define NO_NORMAL_THRESHOLD 0.33

#include "VisualizeCommon.fxh"

#define MAX_STEPS 3

uniform float EscapeVelocity;
uniform float BounceVelocityMultiplier;
uniform float LifeDecayRate;
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

    float3 velocity = applyFrictionAndMaximum(oldVelocity.xyz);

    TVARS vars = makeDistanceFieldConstants();

    float3 scaledVelocity = velocity * DeltaTimeSeconds;

    float oldDistance = SAMPLE(oldPosition.xyz, vars);
    float3 unitVector = normalize(scaledVelocity);
    float stepSpeed = length(scaledVelocity);

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
        float3 normal = estimateNormal(oldPosition.xyz, vars);
        if (length(normal) < NO_NORMAL_THRESHOLD) {
            // HACK to avoid getting stuck at the center of volumes
            float s, c;
            sincos((xy.x / 97) + (xy.y / 17), s, c);
            normal = float3(s, c, 0);
        }

        if (oldDistance >= (CollisionDistance - FALSE_BOUNCE_HACK)) {
            scaledVelocity = (unitVector * stepSpeed);
            newPosition = float4(oldPosition + scaledVelocity, oldPosition.w - LifeDecayRate);
            // We started outside. Bounce away next step if configured to. Otherwise, we'll halt.
            float3 bounceVector = normalize(-(2 * dot(normal, unitVector) * (normal - unitVector)));
            newVelocity = float4(bounceVector * (min(MaximumVelocity, length(velocity.xyz) * BounceVelocityMultiplier)), oldVelocity.w);
            // Deduct the collision life penalty because we just entered a collision state.
            newPosition.w -= CollisionLifePenalty;
        } else {
            // We started inside, so flee at our escape velocity.
            float3 escapeVector = normalize(normal);

            newVelocity = float4(escapeVector * EscapeVelocity, oldVelocity.w);
            scaledVelocity = newVelocity.xyz * DeltaTimeSeconds;
            newPosition = float4(oldPosition + scaledVelocity, oldPosition.w - (LifeDecayRate * DeltaTimeSeconds));
        }
    } else {
        newPosition = float4(oldPosition.xyz + scaledVelocity, oldPosition.w - (LifeDecayRate * DeltaTimeSeconds));
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
