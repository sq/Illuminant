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
#define TRACE_INITIAL_OFFSET_PX 1

// We threshold shadow values from cone tracing to eliminate 'almost obstructed' and 'almost unobstructed' artifacts
#define FULLY_SHADOWED_THRESHOLD 0.075
#define UNSHADOWED_THRESHOLD 0.95

// We manually increase distance samples in order to avoid tiny shadow artifact specks at the edges of surfaces
#define HACK_DISTANCE_OFFSET 1.5

struct TraceParameters {
    float3 start;
    float3 direction;
};

float coneTraceStep(
    // maxRadius, lightTangentAngle, minStepSize, distanceFalloff
    in    float4 config,
    in    float  distanceToObstacle,
    in    float  offset,
    inout float  visibility
) {
    float localSphereRadius = min(
        (config.y * offset) + MIN_CONE_RADIUS, config.x
    );

    visibility = min(visibility, ((distanceToObstacle + HACK_DISTANCE_OFFSET) / localSphereRadius));

    return max(
        // the abs() actually makes this faster somehow, but
        //  it shouldn't ever be needed? :/
        abs(distanceToObstacle) * getLongStepFactor(),
        config.z
    );
}

float coneTrace(
    in float3 lightCenter,
    in float2 lightRamp,
    in float2 coneGrowthFactorAndDistanceFalloff,
    in float3 shadedPixelPosition,
    in DistanceFieldConstants vars
) {
    float  traceLength;
    float3 traceDirection;
    float4 config;

    {
        float3 traceVector = (lightCenter - shadedPixelPosition);
        traceLength = length(traceVector);
        traceDirection = traceVector / traceLength;

        float maxRadius = clamp(
            lightRamp.x, MIN_CONE_RADIUS, getMaxConeRadius()
        );
        float rampLength           = max(lightRamp.y, 16);
        float radiusGrowthPerPixel = maxRadius / rampLength * 
            coneGrowthFactorAndDistanceFalloff.x;

        // maxRadius, lightTangentAngle, minStepSize, distanceFalloff
        config = float4(
            maxRadius, radiusGrowthPerPixel, 
            max(1, getMinStepSize()), 
            coneGrowthFactorAndDistanceFalloff.y
        );
    }

    float a, b;
    a = TRACE_INITIAL_OFFSET_PX;
    b = traceLength;

    bool abort = DistanceField.Extent.x <= 0;
    float stepsRemaining = getStepLimit();
    float visibility = 1.0;

    float aSample, bSample;

    [loop]
    while (!abort) {
        aSample = sampleDistanceFieldEx(shadedPixelPosition + (traceDirection * a), vars);
        bSample = sampleDistanceFieldEx(shadedPixelPosition + (traceDirection * b), vars);

        a += coneTraceStep(config, aSample, a, visibility);
        b -= coneTraceStep(config, bSample, b, visibility);

        stepsRemaining--;
        abort =
            (stepsRemaining <= 0) ||
            (visibility < FULLY_SHADOWED_THRESHOLD) ||
            (a >= b);
    }

    // HACK: Force visibility down to 0 if we are going to terminate the trace because we took too many steps.
    float stepWindowVisibility = stepsRemaining / MAX_STEP_RAMP_WINDOW;
    visibility = min(visibility, stepWindowVisibility);

    return pow(
        clamp(
            clamp((visibility - FULLY_SHADOWED_THRESHOLD), 0, 1) /
            (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD),
            0, 1
        ),
        getOcclusionToOpacityPower()
    );
}