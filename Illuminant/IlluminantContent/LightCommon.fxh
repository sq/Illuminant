#define DOT_OFFSET     0.0
#define DOT_RAMP_RANGE 0.5
#define DOT_EXPONENT   0.9
#define DISTANCE_FUDGE 1.1

uniform float GroundZ;
uniform float ZToYMultiplier;

uniform float  RenderScale;
uniform float  GBufferInvScaleFactor;
uniform float2 GBufferTexelSize;

Texture2D GBuffer      : register(t2);
sampler GBufferSampler : register(s2) {
    Texture = (GBuffer);
    AddressU  = CLAMP;
    AddressV  = CLAMP;
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
};

// returns world position data from the gbuffer at the specified screen position
void sampleGBuffer(
    float2 screenPositionPx,
    out float3 worldPosition,
    out float3 normal
) {
    // FIXME: Should we be offsetting distance field samples too?
    float2 uv     = (screenPositionPx + 0.5) * GBufferTexelSize;

    float4 sample = tex2Dlod(GBufferSampler, float4(uv, 0, 0));

    worldPosition = float3(screenPositionPx.x / RenderScale, sample.z, sample.w);

    // HACK: Reconstruct the y normal from the z normal
    float normalZ = (sample.y - 0.5) * 2;
    normal = normalize(float3(
        (sample.x - 0.5) * 2, 1 - abs(normalZ), normalZ
    ));
}

float computeLightOpacity(
    float3 shadedPixelPosition, float3 shadedPixelNormal,
    float3 lightCenter, float lightRadius, float lightRampLength, float exponential
) {
    float3 distance3      = shadedPixelPosition - lightCenter;
    float  distance       = length(distance3);
    float3 distanceVector = distance3 / distance;
    float  distanceFactor = 1 - clamp((distance - lightRadius) / lightRampLength, 0, 1);

    if (exponential)
        distanceFactor *= distanceFactor;

    /*

    float  d            = dot(-distanceVector, shadedPixelNormal);
    // HACK: We allow the light to be somewhat behind the surface without occluding it,
    //  and we want a smooth ramp between occluded and not-occluded
    float  normalFactor = pow(clamp((d + DOT_OFFSET) / DOT_RAMP_RANGE, 0, 1), DOT_EXPONENT);
    // HACK: * would be more accurate
    return min(normalFactor, distanceFactor);
    */

    return distanceFactor;
}
