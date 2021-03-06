#pragma fxcparams(/O3 /Zi)

#define INCLUDE_RAMPS
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "Bezier.fxh"
#include "ParticleCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "UpdateCommon.fxh"

#define SAMPLE sampleDistanceFieldEx
#define TVARS  DistanceFieldConstants
#define TRACE_MIN_STEP_SIZE 2
#define TRACE_FINAL_MIN_STEP_SIZE 12
#define NO_NORMAL_THRESHOLD 0.33
#define MAX_STEP_COUNT 3
#define BOUNCE_DELAY 3

// When checking whether we are escaping we mask out the z axis since in many 
//  cases escaping via the Z axis will fail (many distance fields are truncated at top/bottom)
#define ESCAPE_MASK float3(1, 1, 0)

// When spawned inside an obstruction we start at a fraction of the escape speed
//  and then accelerate over time while we are still trapped, up to the limit
#define INITIAL_ESCAPE_SPEED 0.33
#define ESCAPE_SPEED_ACCELERATION 1.1

#include "VisualizeCommon.fxh"

void PS_Update (
    ACCEPTS_VPOS,
    out float4 resultPosition : COLOR0,
    out float4 newVelocity    : COLOR1,
    out float4 renderColor    : COLOR2,
    out float4 renderData     : COLOR3
) {
    resultPosition = newVelocity = renderColor = renderData = 0;

    float2 xy = GET_VPOS;
    float4 oldPosition, oldVelocity, attributes;
    readStateOrDiscard(
        xy, oldPosition, oldVelocity, attributes
    );

    float newLife = oldPosition.w - (getLifeDecayRate() * getDeltaTimeSeconds());
    if (newLife <= 0) {
        newVelocity = 0;
        resultPosition = 0;
        renderColor = 0;
        renderData = 0;
        return;
    }

    float3 unitVector = normalize(oldVelocity.xyz);
    float3 velocity = applyFrictionAndMaximum(oldVelocity.xyz);

    TVARS vars = makeDistanceFieldConstants();

    bool collided = false, escaping = false;
    float3 scaledVelocity = velocity * getDeltaTimeSeconds();

    float3 previousPosition = oldPosition.xyz, collisionPosition = 0, newPosition = previousPosition;

    float initialDistance = SAMPLE(oldPosition.xyz, vars);
    bool wasColliding = initialDistance < getCollisionDistance();
    float travelDistance = max(0, min(initialDistance, length(scaledVelocity)));
    int stepCount = MAX_STEP_COUNT;
    if (wasColliding)
        stepCount = 1;
    else if (travelDistance <= 0.001)
        stepCount = 0;

    for (int i = 0; i < stepCount; i++) {
        float3 testPosition = oldPosition.xyz + (travelDistance * unitVector);
        float stepDistance = SAMPLE(testPosition, vars);
        if (stepDistance < getCollisionDistance()) {
            collided = true;
            collisionPosition = testPosition;
        }
        escaping = stepDistance > initialDistance;

        if (collided && !escaping) {
            collisionPosition = testPosition;
            float offset = clamp(stepDistance + getCollisionDistance(), 0.05, 16);
            travelDistance = max(0, travelDistance - offset);
        } else
            stepCount = 0;

        if (travelDistance <= 0.001)
            stepCount = 0;
    }

    REQUIRE_BRANCH // estimating normals is expensive
    if (collided) {
        bool bounce = oldVelocity.w <= 0;
        bool redirect = wasColliding && !escaping;

        float3 normal = 0;
        if (bounce || redirect)
            normal = estimateNormal4(collisionPosition, vars);

        float escapeSpeed = min(getMaximumVelocity(), getEscapeVelocity());

        if (redirect) {
            normal *= ESCAPE_MASK;
            if (length(normal) < NO_NORMAL_THRESHOLD) {
                // HACK to avoid getting stuck at the center of volumes
                float s, c;
                sincos((xy.x / 67) + (xy.y / 13), s, c);
                normal = float3(s, c, 0);
            }
            // We started inside and are not escaping, so flee at our escape velocity.
            float3 escapeVector = normalize(normal);
            // FIXME: We use velocity.w for the particle type now so this seems bad
            newVelocity = float4(escapeVector * escapeSpeed * INITIAL_ESCAPE_SPEED, BOUNCE_DELAY);
            float3 escapeDelta = newVelocity.xyz * getDeltaTimeSeconds();
            newPosition = oldPosition.xyz + escapeDelta;
        } else if (bounce) {
            // We started outside. Bounce away next step if configured to. Otherwise, we'll halt.
            float3 bounceVector = -(2 * dot(normal, unitVector) * (normal - unitVector));
            // HACK to avoid getting stuck
            if (length(bounceVector) < NO_NORMAL_THRESHOLD)
                bounceVector = -unitVector;
            else
                bounceVector = normalize(bounceVector);
            newPosition = collisionPosition;
            newVelocity = float4(bounceVector * (min(getMaximumVelocity(), length(velocity.xyz) * getBounceVelocityMultiplier())), BOUNCE_DELAY);
            // Deduct the collision life penalty because we just entered a collision state.
            newLife -= getCollisionLifePenalty();
        } else {
            // We're escaping so keep going
            float currentSpeed = length(oldVelocity.xyz);
            float newSpeed = max(currentSpeed * ESCAPE_SPEED_ACCELERATION, escapeSpeed);
            newVelocity.xyz = unitVector * newSpeed;
            newPosition = oldPosition.xyz + (travelDistance * unitVector);
        }
    } else {
        newVelocity = float4(velocity, max(oldVelocity.w - 1, 0));
        newPosition = oldPosition.xyz + (travelDistance * unitVector);
    }

    if (newLife <= 0) {
        newPosition = 0;
        newVelocity = 0;
    }
    resultPosition = float4(newPosition, newLife);
    computeRenderData(xy, resultPosition, newVelocity, attributes, renderColor, renderData);
}

technique UpdateWithDistanceField {
    pass P0
    {
        vertexShader = compile vs_3_0 VS_Update();
        pixelShader = compile ps_3_0 PS_Update();
    }
}
