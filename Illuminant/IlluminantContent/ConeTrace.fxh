// The minimum and maximum approximate cone tracing radius
// The radius increases as the cone approaches the light source
// Raising the maximum produces soft shadows, but if it's too large you will get artifacts.
// A larger minimum increases the size of the AO 'blobs' around distant obstructions.
#define MIN_CONE_RADIUS 0.5
#define MAX_ANGLE_DEGREES 10
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

struct TraceParameters {
    float3 start;
    float3 direction;
};

void coneTraceStep(
    // maxRadius, lightTangentAngle, minStepSize
    in    float3 config,
    in    float  sign,
    in    float  distanceToObstacle,
    inout float  offset,
    inout float  visibility
) {
    float localSphereRadius = min(
        (config.y * offset) + MIN_CONE_RADIUS, config.x
    );

    float localVisibility = distanceToObstacle / localSphereRadius;
    visibility = min(visibility, localVisibility);

    float stepSize = max(
        abs(distanceToObstacle) * (
            // Steps outside of objects can be scaled to be longer/shorter to adjust the quality
            //  of partial visibility areas
            (distanceToObstacle < 0)
                ? 1
                : DistanceField.Step.z
        ), config.z
    );

    offset += stepSize * sign;
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

    float  traceLength;
    float3 traceDirection;
    float3 config;

    {
        float3 traceVector = (lightCenter - shadedPixelPosition);
        traceLength = length(traceVector);
        traceDirection = traceVector / traceLength;

        float maxTangentAngle = tan(MAX_ANGLE_DEGREES * PI / 180.0f);
        float lightTangentAngle = min(lightRamp.x / traceLength, maxTangentAngle);

        float maxRadius = clamp(
            lightRamp.x, MIN_CONE_RADIUS, DistanceField.MaxConeRadius
        );

        config = float3(
            maxRadius, lightTangentAngle, max(1, DistanceField.Step.y)
        );
    }

    float center = traceLength / 2.0;
    float a, b, c, d;
    a = TRACE_INITIAL_OFFSET_PX;
    b = c = center;
    d = traceLength;

    bool abort = false, abort1 = false, abort2 = false;
    float stepCount = 0;
    float visibility = 1.0;

    float aSample, bSample, cSample, dSample;

    [loop]
    while (!abort) {
        aSample = sampleDistanceField(shadedPixelPosition + (traceDirection * a), vars);
        bSample = sampleDistanceField(shadedPixelPosition + (traceDirection * b), vars);
        cSample = sampleDistanceField(shadedPixelPosition + (traceDirection * c), vars);
        dSample = sampleDistanceField(shadedPixelPosition + (traceDirection * d), vars);

        [branch]
        if (!abort1) {
            coneTraceStep(config, 1, aSample, a, visibility);
            coneTraceStep(config, -1, bSample, b, visibility);
        }

        [branch]
        if (!abort2) {
            coneTraceStep(config, 1, cSample, c, visibility);
            coneTraceStep(config, -1, dSample, d, visibility);
        }

        abort1 = (a >= b);
        abort2 = (c >= d);

        stepCount += 1;
        abort =
            (stepCount >= DistanceField.Step.x) ||
            (visibility < FULLY_SHADOWED_THRESHOLD) ||
            (abort1 && abort2);
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