#define SMALL_STEP_FACTOR 1
#define EPSILON 0.5
#ifndef OUTLINE_SIZE
    #define OUTLINE_SIZE 1.8
#endif
#ifndef FILL_INTERIOR
    #define FILL_INTERIOR false
#endif
#ifndef VISUALIZE_TEXEL
    #define VISUALIZE_TEXEL (float4( \
        getInvScaleFactorX(), \
        getInvScaleFactorY(), \
        DistanceField.Extent.z / max(DistanceField.TextureSliceCount.w, 1), \
        0 \
    ))
#endif

float3 estimateNormal3(
    float3   position,
    in TVARS vars
) {
    // We want to stagger the samples so that we are moving a reasonable distance towards the nearest texel
    // FIXME: Pick better constant when sampling a distance function
    float4 texel = VISUALIZE_TEXEL;
    float3 result = 0;

    [loop]
    for (int i = 0; i < 3; i++) {
        float3 mask = float3(1, 0, 0);
        if (i >= 1)
            mask = float3(0, 1, 0);
        if (i >= 2)
            mask = float3(0, 0, 1);

        float3 offset = texel.xyz * mask;
        float axis = SAMPLE(position + offset, vars) - SAMPLE(position - offset, vars);

        result += axis * mask;
    }

    return normalize(result);
}

static float2 normalK = float2(1,-1);
static float3 normalWeights[] = {normalK.xyy, normalK.yyx, normalK.yxy, normalK.xxx};

float3 estimateNormal4(
    float3   position,
    in TVARS vars
) {
    // We want to stagger the samples so that we are moving a reasonable distance towards the nearest texel
    // FIXME: Pick better constant when sampling a distance function
    float4 texel = VISUALIZE_TEXEL;

    float3 result = 0;
    [loop]
    for (int i = 0; i < 4; i++) {
        float3 weight = normalWeights[i];
        result += weight * SAMPLE(position + weight * texel.rgb, vars);
    }

    return normalize(result);
}

bool traceSurface (
    float3     rayStart,
    float3     rayVector,
    out float  intersectionDistance,
    out float3 estimatedIntersection,
    in TVARS   vars
) {
    float positionAlongRay = 0;
    float rayLength = max(0.001, length(rayVector));
    float3 rayDirection = rayVector / rayLength;

    [loop]
    while (positionAlongRay <= rayLength) {
        float3 samplePosition = rayStart + (rayDirection * positionAlongRay);
        float distance = SAMPLE(samplePosition, vars);

        float minStepSize = max(TRACE_MIN_STEP_SIZE, (positionAlongRay / rayLength) * TRACE_FINAL_MIN_STEP_SIZE);

        if (distance <= minStepSize) {
            // HACK: Estimate a likely intersection point
            intersectionDistance = positionAlongRay + distance;
            estimatedIntersection = rayStart + (rayDirection * intersectionDistance);
            positionAlongRay = rayLength;
            return true;
        }

        float stepSize = max(minStepSize, abs(distance) * SMALL_STEP_FACTOR);
        positionAlongRay += stepSize;
    }

    intersectionDistance = -1;
    estimatedIntersection = 0;
    return false;
}

float traceOutlines (
    float3   rayStart,
    float3   rayVector,
    in TVARS vars
) {
    float closestDistance = 99999;

    float positionAlongRay = 0;
    float rayLength = length(rayVector);
    float3 rayDirection = rayVector / rayLength;

    [loop]
    while (positionAlongRay <= rayLength) {
        float3 samplePosition = rayStart + (rayDirection * positionAlongRay);
        float distance = SAMPLE(samplePosition, vars);

        closestDistance = min(distance, closestDistance);

        if (FILL_INTERIOR) {
            if (distance <= 1)
                return 1;
        } else {
            if (distance < -OUTLINE_SIZE)
                break;
        }

        float minStepSize = max(2.5, (positionAlongRay / rayLength) * 12);
        float stepSize = max(minStepSize, abs(distance) * SMALL_STEP_FACTOR);
        positionAlongRay += stepSize;
    }

    float a = 1.0 - abs(clamp(closestDistance - 1, -OUTLINE_SIZE, OUTLINE_SIZE) / OUTLINE_SIZE);
    return a * a;
}