struct TraceInfo {
    float3 start;
    float3 direction;
    float  length;
};

void coneTraceStep(
    in    TraceInfo trace,
    in    float3 sphereRadiusSettings,
    in    TraceVars vars,
    inout float  traceOffset,
    inout float  visibility,
    inout float  minStepSize,
    inout float  localSphereRadius
) {
    float3 samplePosition = trace.start + (trace.direction * traceOffset);

    float distanceToObstacle = sampleDistanceField(
        samplePosition, vars
    );

    float localVisibility =
        distanceToObstacle / localSphereRadius;
    visibility = min(visibility, localVisibility);

    float stepSize = max(
        abs(distanceToObstacle) * (
            // Steps outside of objects can be scaled to be longer/shorter to adjust the quality
            //  of partial visibility areas
            (distanceToObstacle < 0)
            ? 1
            : DistanceField.Step.w
            ), minStepSize
        );

    traceOffset = traceOffset + stepSize;

    minStepSize = (DistanceField.Step.z * stepSize) + minStepSize;

    // Sadly doing this with the reciprocal instead doesn't work :|
    localSphereRadius = min(
        sphereRadiusSettings.y,
        (sphereRadiusSettings.z * stepSize) + localSphereRadius
    );
}

float coneTrace(
    in float3 lightCenter,
    in float2 lightRamp,
    in float3 shadedPixelPosition
) {
    TraceVars vars = {
        DistanceField.TextureSliceCount.z - 1,
        1.0 / DistanceField.TextureSliceCount.x,
        1.0 / DistanceField.Extent.z
    };

    TraceInfo trace;
    float traceOffset = TRACE_INITIAL_OFFSET_PX;
    trace.start = shadedPixelPosition;
    float3 traceVector = (lightCenter - trace.start);
    trace.length = length(traceVector);
    trace.direction = normalize(traceVector);

    float maxTangentAngle = tan(MAX_ANGLE_DEGREES * PI / 180.0f);
    float lightTangentAngle = min(lightRamp.x / trace.length, maxTangentAngle);

    float minRadius = max(MIN_CONE_RADIUS, 0.1);
    float maxRadius = clamp(
        lightRamp.x, minRadius, DistanceField.MaxConeRadius
    );
    float3 sphereRadiusSettings = float3(
        minRadius, maxRadius, lightTangentAngle
    );

    float minStepSize = max(1, DistanceField.Step.y);
    float localSphereRadius = minRadius;
    float visibility = 1.0;

    bool abort = false;

    float stepCount = 0;

    [loop]
    while (!abort) {
        abort =
            (stepCount >= DistanceField.Step.x) ||
            (traceOffset >= trace.length) ||
            (visibility < FULLY_SHADOWED_THRESHOLD);
        if (abort)
            traceOffset = trace.length;

        coneTraceStep(
            trace, sphereRadiusSettings, vars,
            traceOffset, visibility,
            minStepSize, localSphereRadius
        );
        stepCount += 1;
    }

    // HACK: Force visibility down to 0 if we are going to terminate the trace because we took too many steps.
    float windowStart = max(DistanceField.Step.x - MAX_STEP_RAMP_WINDOW, 0);
    float stepWindowVisibility = (1.0 - (stepCount - windowStart) / MAX_STEP_RAMP_WINDOW);
    visibility = min(visibility, stepWindowVisibility);

    return pow(
        clamp(
            clamp((visibility - FULLY_SHADOWED_THRESHOLD), 0, 1) /
            (UNSHADOWED_THRESHOLD - FULLY_SHADOWED_THRESHOLD),
            0, 1
        ),
        DistanceField.OcclusionToOpacityPower
    );
}