#define PI 3.14159265359

// This is a distance of 0
// Moving this value up/down allocates more precision to positive or negative distances
#define DISTANCE_ZERO (192.0 / 255.0)

// Maximum distance
// Smaller values increase the precision of distance values but slow down traces
#define DISTANCE_MAX 512

// Filtering dramatically increases the precision of the distance field,
//  *and* it's mathematically correct!
#define DISTANCE_FIELD_FILTER LINEAR

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


float closestPointOnEdgeAsFactor (
    float2 pt, float2 edgeStart, float2 edgeEnd
) {
    float2 edgeDelta = edgeEnd - edgeStart;
    float  edgeLength = length(edgeDelta);
    edgeLength *= edgeLength;

    float2 pointDelta = (pt - edgeStart) * edgeDelta;
    return (pointDelta.x + pointDelta.y) / edgeLength;
}

float2 closestPointOnEdge (
    float2 pt, float2 edgeStart, float2 edgeEnd
) {
    float u = closestPointOnEdgeAsFactor(pt, edgeStart, edgeEnd);
    return edgeStart + ((edgeEnd - edgeStart) * clamp(u, 0, 1));
}

float4 encodeDistance (float distance) {
    return DISTANCE_ZERO - (distance / DISTANCE_MAX);
}

float decodeDistance (float encodedDistance) {
    return (DISTANCE_ZERO - encodedDistance) * DISTANCE_MAX;
}

struct DistanceFieldSettings {
    // StepLimit, MinimumLength, MinimumLengthGrowthRate, LongFactor
    float4 Step;

    float  MaxConeRadius;
    float  OcclusionToOpacityPower;
    float  InvScaleFactor;
    float3 Extent;
    float3 TextureSliceCount;
    float2 TextureSliceSize;
    float2 TextureTexelSize;
};

uniform DistanceFieldSettings DistanceField;

Texture2D DistanceFieldTexture        : register(t1);
sampler   DistanceFieldTextureSampler : register(s1) {
    Texture = (DistanceFieldTexture);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = DISTANCE_FIELD_FILTER;
    MagFilter = DISTANCE_FIELD_FILTER;
};

struct TraceInfo {
    float3 start;
    float3 direction;
    float  length;
};

struct TraceVars {
    float sliceCountZMinus1;
    float invSliceCountX;
    float invDistanceFieldExtentZ;
};

float2 computeDistanceFieldSliceUv (
    float coarseSliceIndex, 
    TraceVars vars
) {
    float rowIndexF   = coarseSliceIndex * vars.invSliceCountX;
    float rowIndex    = floor(rowIndexF);
    float columnIndex = floor((rowIndexF - rowIndex) * DistanceField.TextureSliceCount.x);
    float2 indexes = float2(columnIndex, rowIndex);
    return indexes * DistanceField.TextureSliceSize.xy;
}

float2 computeDistanceFieldSubsliceUv (
    float2 positionPx
) {
    // HACK: Ensure we don't sample outside of the slice (filtering! >:()
    // FIXME: Why is this 1 and not 0.5?
    // FIXME: Should we be offsetting the position like we do with gbuffer reads?
    return clamp(positionPx + 0.5, 1, DistanceField.Extent.xy - 1) * DistanceField.TextureTexelSize;
}

// FIXME: I hard-coded this below because I'm a bad person
static const int packedSliceCount = 3;

float sampleDistanceField (
    float3 position, 
    TraceVars vars
) {
    // Interpolate between two Z samples. The xy interpolation is done by the GPU for us.
    // linear [0-ZMax] -> linear [0-1]
    float linearPositionZ = position.z * vars.invDistanceFieldExtentZ;
    // linear [0-1] -> [0-NumZSlices)
    float slicePosition = clamp(linearPositionZ * DistanceField.TextureSliceCount.z, 0, vars.sliceCountZMinus1);
    float virtualSliceIndex = floor(slicePosition);

    float physicalSliceIndex = clamp(virtualSliceIndex, 0, vars.sliceCountZMinus1) * (1.0 / packedSliceCount);
   
    float4 uv = float4(
        computeDistanceFieldSubsliceUv(position.xy) +
          computeDistanceFieldSliceUv(physicalSliceIndex, vars),
        0, 0
    );

    float4 packedSample = tex2Dlod(DistanceFieldTextureSampler, uv);

    float maskPatternIndex = virtualSliceIndex % packedSliceCount;
    float sample1, sample2;

    // This is hard-coded for three slices (r/g/b)
    {
        if (maskPatternIndex >= 2) {
            sample1 = packedSample.b;
            sample2 = packedSample.a;
        } else if (maskPatternIndex >= 1) {
            sample1 = packedSample.g;
            sample2 = packedSample.b;
        } else {
            sample1 = packedSample.r;
            sample2 = packedSample.g;
        }
    }

    float subslice = slicePosition - virtualSliceIndex;

    float blendedSample = lerp(
        sample1, sample2, subslice
    );

    float decodedDistance = decodeDistance(blendedSample);

    // HACK: Samples outside the distance field will be wrong if they just
    //  read the closest distance in the field.
    float3 clampedPosition = clamp(position, 0, DistanceField.Extent);
    float distanceToVolume = length(clampedPosition - position);

    return decodedDistance + distanceToVolume;
}

void coneTraceStep (
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

float coneTrace (
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