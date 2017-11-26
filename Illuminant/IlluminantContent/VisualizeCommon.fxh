#define SMALL_STEP_FACTOR 1
#define EPSILON 0.5
#define OUTLINE_SIZE 1.8

float3 estimateNormal(
    float3   position,
    in TVARS vars
) {
    // We want to stagger the samples so that we are moving a reasonable distance towards the nearest texel
    // FIXME: Pick better constant when sampling a distance function
    float4 texel = float4(
        DistanceField.InvScaleFactor,
        DistanceField.InvScaleFactor,
        DistanceField.Extent.z / DistanceField.TextureSliceCount.z,
        0
    );

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

bool traceSurface (
    float3     rayStart,
    float3     rayVector,
    out float  intersectionDistance,
    out float3 estimatedIntersection,
    in TVARS   vars
) {
    float positionAlongRay = 0;
    float rayLength = length(rayVector);
    float3 rayDirection = rayVector / rayLength;

    [loop]
    while (positionAlongRay <= rayLength) {
        float3 samplePosition = rayStart + (rayDirection * positionAlongRay);
        float distance = SAMPLE(samplePosition, vars);

        float minStepSize = max(2.5, (positionAlongRay / rayLength) * 12);

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

        if (distance < -OUTLINE_SIZE)
            break;

        float minStepSize = max(2.5, (positionAlongRay / rayLength) * 12);
        float stepSize = max(minStepSize, abs(distance) * SMALL_STEP_FACTOR);
        positionAlongRay += stepSize;
    }

    float a = 1.0 - abs(clamp(closestDistance - 1, -OUTLINE_SIZE, OUTLINE_SIZE) / OUTLINE_SIZE);
    return a * a;
}