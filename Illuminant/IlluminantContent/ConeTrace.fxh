// The minimum and maximum approximate cone tracing radius
// The radius increases as the cone approaches the light source
// Raising the maximum produces soft shadows, but if it's too large you will get artifacts.
// A larger minimum increases the size of the AO 'blobs' around distant obstructions.
#define MIN_CONE_RADIUS 0.33
// See uniforms for the other two constants

// As we approach the maximum number of steps we ramp visibility down to 0.
// Otherwise, we get gross 'false visibility' artifacts inside early-terminated traces
//  (most, if not all, early-terminated traces are occluded in practice)
#define MAX_STEP_RAMP_WINDOW 2

// HACK: Start the trace a certain number of pixels (along the trace) away from the shaded point.
// This mitigates erroneous self-occlusion
// This works better if you offset the shaded point forwards along the surface normal.
#define TRACE_INITIAL_OFFSET_PX 0.5

// We threshold shadow values from cone tracing to eliminate 'almost obstructed' and 'almost unobstructed' artifacts
#define FULLY_SHADOWED_THRESHOLD 0.075
#define UNSHADOWED_THRESHOLD 0.95

// We manually increase distance samples in order to avoid tiny shadow artifact specks at the edges of surfaces
#define HACK_DISTANCE_OFFSET 1.5

// The distance between the A and B points during a bidirectional cone trace is multiplied by this amount
//  and if that exceeds 1 then the trace is terminated.
#define TRACE_MEET_THRESHOLD 500

#define TRACE_END_MULTIPLIER 100

struct TraceState {
    float3 origin, direction;
    // position, length, visibility
    float3 data;
};

void coneTraceInitialize (
    out TraceState state,
    in float3 startPosition, in float3 endPosition,
    in float startOffset, in float lightRadius, in bool startAtEnd
) {
    float3 traceVector = (endPosition - startPosition);
    float traceLength = length(traceVector);
    state.origin = startPosition;
    state.direction = traceVector / traceLength;
    state.data.y = max(traceLength - lightRadius, 1);
    state.data.x = startAtEnd ? traceLength - startOffset : startOffset;
    state.data.z = 1.0;
}

float coneTraceStep (
    // maxRadius, lightTangentAngle, minStepSize, distanceFalloff
    in    float4 config,
    in    float  distanceToObstacle,
    in    float  offset,
    inout float  visibility
) {
    float localSphereRadius = min(
        (config.y * offset) + MIN_CONE_RADIUS, config.x
    );

    float localVisibility = ((distanceToObstacle + HACK_DISTANCE_OFFSET) / localSphereRadius);
    visibility = min(visibility, localVisibility);

    return max(
        // the abs() actually makes this faster somehow, but
        //  it shouldn't ever be needed? :/
        abs(distanceToObstacle) * getLongStepFactor(),
        config.z
    );
}

float coneTraceAdvance (
    inout TraceState state,
    in    float4 config,
    in    DistanceFieldConstants vars
) {
    float sample = sampleDistanceFieldEx(state.origin + (state.direction * state.data.x), vars);

    state.data.x += coneTraceStep(config, sample, state.data.x, state.data.z);
    return saturate(state.data.z - FULLY_SHADOWED_THRESHOLD) * saturate(state.data.y - state.data.x);
}

float coneTraceAdvanceEx (
    inout TraceState state,
    in float4 config,
    in DistanceFieldConstants vars
) {
    float sample = sampleDistanceFieldEx(state.origin + (state.direction * state.data.x), vars);

    state.data.x = min(
        state.data.x + coneTraceStep(config, sample, state.data.x, state.data.z),
        state.data.y
    );
    return saturate(state.data.z - FULLY_SHADOWED_THRESHOLD) * saturate((state.data.y - state.data.x) * TRACE_END_MULTIPLIER);
}

float coneTraceAdvance2 (
    inout TraceState traceA,
    inout TraceState traceB,
    in    float4 config,
    in    DistanceFieldConstants vars
) {
    float3 samplePos = traceA.origin + (traceA.direction * traceA.data.x);
    float sample = sampleDistanceFieldEx(samplePos, vars);
    float stepResult = coneTraceStep(config, sample, traceA.data.x, traceA.data.z);
    traceA.data.x += stepResult;

    samplePos = traceB.origin + (traceB.direction * traceB.data.x);
    sample = sampleDistanceFieldEx(samplePos, vars);
    stepResult = coneTraceStep(config, sample, traceB.data.x, traceB.data.z);
    traceB.data.x -= stepResult;

    float result = saturate(min(traceA.data.z, traceB.data.z) - FULLY_SHADOWED_THRESHOLD);
    return result;
}

float coneTraceFinish (in TraceState state) {
    return state.data.z;
}

float4 createTraceConfig (
    in float2 lightRamp, 
    in float2 coneGrowthFactorAndDistanceFalloff
) {
    float maxRadius = clamp(
        lightRamp.x, MIN_CONE_RADIUS, getMaxConeRadius()
    );
    float rampLength           = max(lightRamp.y, 16);
    float radiusGrowthPerPixel = maxRadius / rampLength * 
        coneGrowthFactorAndDistanceFalloff.x;

    // maxRadius, lightTangentAngle, minStepSize, distanceFalloff
    return float4(
        maxRadius, radiusGrowthPerPixel, 
        max(1, getMinStepSize()), 
        coneGrowthFactorAndDistanceFalloff.y
    );
}

float coneTrace (
    in float3 lightCenter,
    // radius, ramp length
    in float2 lightRamp,
    in float2 coneGrowthFactorAndDistanceFalloff,
    in float3 shadedPixelPosition,
    in DistanceFieldConstants vars,
    in bool   enable
) {
    TraceState traceA;
    coneTraceInitialize(
        traceA, shadedPixelPosition, lightCenter,
        TRACE_INITIAL_OFFSET_PX, lightRamp.x, false
    );

    float4 config = createTraceConfig(lightRamp, coneGrowthFactorAndDistanceFalloff);

    float stepsRemaining = getStepLimit();
    float liveness = (DistanceField.Extent.x > 0) && enable;
    float stepLiveness;

    [loop]
    while (liveness > 0) {
        stepsRemaining--;
        if (stepsRemaining == 0)
            traceA.data.x = traceA.data.y;

        stepLiveness = coneTraceAdvance(traceA, config, vars);

        liveness = stepsRemaining * 
            stepLiveness;
    }

    // HACK: Force visibility down to 0 if we are going to terminate the trace because we took too many steps.
    float stepWindowVisibility = stepsRemaining / MAX_STEP_RAMP_WINDOW;
    float visibility = min(
        coneTraceFinish(traceA), 
        stepWindowVisibility
    );

    float finalResult = pow(
        saturate(
            saturate((visibility - FULLY_SHADOWED_THRESHOLD)) /
            (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD)
        ),
        getOcclusionToOpacityPower()
    );

    return enable ? finalResult : 1.0;
}