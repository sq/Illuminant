struct TraceParameters {
    float3 start;
    float3 direction;
    float  length;
    float3 radiusSettings;
};

struct TraceState {
    float offset;
    float minStepSize;
    float localSphereRadius;
};

void coneTraceStep(
    in    TraceParameters        trace,
    in    DistanceFieldConstants vars,
    inout TraceState             state,
    in    float                  sign,
    inout float                  visibility
) {
    float3 samplePosition = trace.start + (trace.direction * clamp(state.offset, 0, trace.length));

    float distanceToObstacle = sampleDistanceField(
        samplePosition, vars
    );

    float localVisibility =
        distanceToObstacle / clamp(state.localSphereRadius, trace.radiusSettings.x, trace.radiusSettings.y);
    visibility = min(visibility, localVisibility);

    float stepSize = max(
        abs(distanceToObstacle) * (
            // Steps outside of objects can be scaled to be longer/shorter to adjust the quality
            //  of partial visibility areas
            (distanceToObstacle < 0)
            ? 1
            : DistanceField.Step.w
            ), state.minStepSize
        );

    float signedStepSize = stepSize * sign;
    state.offset = state.offset + signedStepSize;

    state.minStepSize = (DistanceField.Step.z * stepSize) + state.minStepSize;

    // Sadly doing this with the reciprocal instead doesn't work :|
    state.localSphereRadius = (trace.radiusSettings.z * signedStepSize) + state.localSphereRadius;
}

float coneTrace(
    in float3 lightCenter,
    in float2 lightRamp,
    in float3 shadedPixelPosition
) {
    DistanceFieldConstants vars = {
        DistanceField.TextureSliceCount.z - 1,
        1.0 / DistanceField.TextureSliceCount.x,
        1.0 / DistanceField.Extent.z
    };

    TraceParameters trace;
    {
        trace.start = shadedPixelPosition;
        float3 traceVector = (lightCenter - trace.start);
        trace.length = length(traceVector);
        trace.direction = normalize(traceVector);
    }

    {
        float maxTangentAngle = tan(MAX_ANGLE_DEGREES * PI / 180.0f);
        float lightTangentAngle = min(lightRamp.x / trace.length, maxTangentAngle);

        float minRadius = max(MIN_CONE_RADIUS, 0.1);
        float maxRadius = clamp(
            lightRamp.x, minRadius, DistanceField.MaxConeRadius
        );

        trace.radiusSettings = float3(
            minRadius, maxRadius, lightTangentAngle
        );
    }

    TraceState head, tail;
    head.offset = TRACE_INITIAL_OFFSET_PX;
    head.minStepSize = max(1, DistanceField.Step.y);
    head.localSphereRadius = trace.radiusSettings.x;

    tail.offset = trace.length;
    tail.minStepSize = head.minStepSize;
    tail.localSphereRadius = trace.radiusSettings.x + (trace.radiusSettings.z * trace.length);

    bool abort = false;
    float stepCount = 0;
    float visibility = 1.0;

    [loop]
    while (!abort) {
        abort =
            (stepCount >= DistanceField.Step.x) ||
            (head.offset >= tail.offset) ||
            (visibility < FULLY_SHADOWED_THRESHOLD);

        coneTraceStep(trace, vars, head, 1, visibility);
        coneTraceStep(trace, vars, tail, -1, visibility);

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