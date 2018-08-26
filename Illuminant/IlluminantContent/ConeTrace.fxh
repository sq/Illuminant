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
#define FULLY_SHADOWED_THRESHOLD 0.1
#define UNSHADOWED_THRESHOLD 0.95

// We manually increase distance samples in order to avoid tiny shadow artifact specks at the edges of surfaces
#define HACK_DISTANCE_OFFSET 2

struct TraceParameters {
    half3 start;
    half3 direction;
};

half coneTraceStep(
    // maxRadius, lightTangentAngle, minStepSize, distanceFalloff
    in    half4 config,
    in    half  distanceToObstacle,
    in    half  offset,
    inout half  visibility
) {
    half localSphereRadius = min(
        (config.y * offset) + MIN_CONE_RADIUS, config.x
    );

    half localVisibility = clamp(((distanceToObstacle + HACK_DISTANCE_OFFSET) / localSphereRadius), 0, 1);
    visibility = min(visibility, localVisibility);

    return max(
        // the abs() actually makes this faster somehow, but
        //  it shouldn't ever be needed? :/
        abs(distanceToObstacle) * getLongStepFactor(),
        config.z
    );
}

half coneTrace(
    in half3 lightCenter,
    in half2 lightRamp,
    in half2 coneGrowthFactorAndDistanceFalloff,
    in half3 shadedPixelPosition,
    in DistanceFieldConstants vars
) {
    half  traceLength;
    half3 traceDirection;
    half4 config;

    {
        half3 traceVector = (lightCenter - shadedPixelPosition);
        traceLength = length(traceVector);
        traceDirection = traceVector / traceLength;

        half maxRadius = clamp(
            lightRamp.x, MIN_CONE_RADIUS, getMaxConeRadius()
        );
        half rampLength           = max(lightRamp.y, 16);
        half radiusGrowthPerPixel = maxRadius / rampLength * 
            coneGrowthFactorAndDistanceFalloff.x;

        // maxRadius, lightTangentAngle, minStepSize, distanceFalloff
        config = half4(
            maxRadius, radiusGrowthPerPixel, 
            max(1, getMinStepSize()), 
            coneGrowthFactorAndDistanceFalloff.y
        );
    }

    half a, b;
    a = TRACE_INITIAL_OFFSET_PX;
    b = traceLength;

    bool abort = DistanceField.Extent.x <= 0;
    half stepCount = 0;
    half visibility = 1.0;

    half aSample, bSample;

    [loop]
    while (!abort) {
        aSample = sampleDistanceField(shadedPixelPosition + (traceDirection * a), vars);
        bSample = sampleDistanceField(shadedPixelPosition + (traceDirection * b), vars);

        a += coneTraceStep(config, aSample, a, visibility);
        b -= coneTraceStep(config, bSample, b, visibility);

        stepCount += 1;
        abort =
            (stepCount >= getStepLimit()) ||
            (visibility < FULLY_SHADOWED_THRESHOLD) ||
            (a >= b);
    }

    // HACK: Force visibility down to 0 if we are going to terminate the trace because we took too many steps.
    half windowStart = max(getStepLimit() - MAX_STEP_RAMP_WINDOW, 0);
    half stepWindowVisibility = (1.0 - (stepCount - windowStart) / MAX_STEP_RAMP_WINDOW);
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