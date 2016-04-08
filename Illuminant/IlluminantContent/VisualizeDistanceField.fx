#include "DistanceFieldCommon.fxh"

#define MIN_STEP_SIZE 3
#define SMALL_STEP_FACTOR 1
#define EPSILON 0.05
#define OUTLINE_SIZE 2

shared float2 ViewportScale;
shared float2 ViewportPosition;

shared float4x4 ProjectionMatrix;
shared float4x4 ModelViewMatrix;

uniform float3 AmbientColor;
uniform float3 LightDirection;
uniform float3 LightColor;

uniform float Time;

float4 ApplyTransform (float3 position) {
    float3 localPosition = ((position - float3(ViewportPosition.xy, 0)) * float3(ViewportScale, 1));
    return mul(mul(float4(localPosition.xyz, 1), ModelViewMatrix), ProjectionMatrix);
}

void VisualizeVertexShader(
    in float3 position       : POSITION0,
    inout float3 rayStart    : POSITION1,
    inout float3 rayVector   : POSITION2,
    out float3 worldPosition : TEXCOORD2,
    out float4 result        : POSITION0
) {
    worldPosition = position;
    float4 transformedPosition = ApplyTransform(position);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float3 estimateNormal (
    float3 position,
    in DistanceFieldConstants vars
) {
    // We want to stagger the samples so that we are moving a reasonable distance towards the nearest texel
    float4 texel = float4(
        DistanceField.InvScaleFactor, 
        DistanceField.InvScaleFactor,
        DistanceField.Extent.z / DistanceField.TextureSliceCount.z, 
        0
    );

    return normalize(float3(
        sampleDistanceField(position + texel.xww, vars) - sampleDistanceField(position - texel.xww, vars),
        sampleDistanceField(position + texel.wyw, vars) - sampleDistanceField(position - texel.wyw, vars),
        sampleDistanceField(position + texel.wwz, vars) - sampleDistanceField(position - texel.wwz, vars)
    ));
}

bool traceRay(
    float3     rayStart,
    float3     rayVector,
    out float  intersectionDistance,
    out float3 estimatedIntersection,
    in DistanceFieldConstants vars
) {
    float positionAlongRay = 0;
    float rayLength = length(rayVector);
    float3 rayDirection = rayVector / rayLength;
    float minStepSize = MIN_STEP_SIZE;

    [loop]
    while (positionAlongRay <= rayLength) {
        float3 samplePosition = rayStart + (rayDirection * positionAlongRay);
        float distance = sampleDistanceField(samplePosition, vars);

        [branch]
        if (distance <= EPSILON) {
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

void ObjectSurfacesPixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart      : POSITION1,
    in  float3 rayVector     : POSITION2,
    in  float2 vpos          : VPOS,
    out float4 result        : COLOR0
) {
    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float intersectionDistance;
    float3 estimatedIntersection;
    if (traceRay(rayStart, rayVector, intersectionDistance, estimatedIntersection, vars)) {
        result = float4(AmbientColor, 1.0);

        float3 normal = estimateNormal(estimatedIntersection, vars);
        float normalDotLight = dot(normal, LightDirection);
        if (normalDotLight > 0)
            result.rgb += LightColor * normalDotLight;
    } else {
        result = 0;
        discard;
    }
}

void ObjectOutlinesPixelShader(
    in  float2 worldPosition : TEXCOORD2,
    in  float3 rayStart : POSITION1,
    in  float3 rayVector : POSITION2,
    in  float2 vpos : VPOS,
    out float4 result : COLOR0
) {
    DistanceFieldConstants vars = makeDistanceFieldConstants();

    float closestDistance = 99999;

    float positionAlongRay = 0;
    float rayLength = length(rayVector);
    float3 rayDirection = rayVector / rayLength;
    float minStepSize = MIN_STEP_SIZE;

    [loop]
    while (positionAlongRay <= rayLength) {
        float3 samplePosition = rayStart + (rayDirection * positionAlongRay);
        float distance = sampleDistanceField(samplePosition, vars);

        closestDistance = min(distance, closestDistance);

        if (distance < -OUTLINE_SIZE)
            break;

        float stepSize = max(MIN_STEP_SIZE, abs(distance) * SMALL_STEP_FACTOR);
        positionAlongRay += stepSize;
    }

    float a = 1.0 - abs(clamp(closestDistance, -OUTLINE_SIZE, OUTLINE_SIZE) / OUTLINE_SIZE);
    a *= a;

    result = float4(a, a, a, a);

    if (a <= 0)
        discard;
}

technique ObjectSurfaces {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader  = compile ps_3_0 ObjectSurfacesPixelShader();
    }
}

technique ObjectOutlines {
    pass P0
    {
        vertexShader = compile vs_3_0 VisualizeVertexShader();
        pixelShader = compile ps_3_0 ObjectOutlinesPixelShader();
    }
}