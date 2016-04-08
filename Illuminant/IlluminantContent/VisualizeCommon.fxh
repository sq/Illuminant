#define SMALL_STEP_FACTOR 1
#define EPSILON 0.5
#define OUTLINE_SIZE 1.8

uniform float3 AmbientColor;
uniform float3 LightDirection;
uniform float3 LightColor;

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

    return normalize(float3(
        SAMPLE(position + texel.xww, vars) - SAMPLE(position - texel.xww, vars),
        SAMPLE(position + texel.wyw, vars) - SAMPLE(position - texel.wyw, vars),
        SAMPLE(position + texel.wwz, vars) - SAMPLE(position - texel.wwz, vars)
    ));
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

        [branch]
        if (distance <= minStepSize) {
            // HACK: Estimate a likely intersection point
            intersectionDistance = positionAlongRay + distance;
            estimatedIntersection = rayStart + (rayDirection * intersectionDistance);
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