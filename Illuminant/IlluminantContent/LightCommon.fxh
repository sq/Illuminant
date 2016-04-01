#define DOT_OFFSET     0.5
#define DOT_RAMP_RANGE 0.2
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
    float3 lightCenter, float lightRadius, float lightRampLength
) {
    float3 distance3      = shadedPixelPosition - lightCenter;
    float  distance       = length(distance3) - lightRadius;
    float  distanceFactor = 1 - clamp(distance / lightRampLength, 0, 1);

    // HACK: Do a fudged dot product test to ramp out light coming from behind a surface
    float3 fudgedDistance = normalize(distance3);
    if (distance3.z >= -(DISTANCE_FUDGE + lightRadius))
        fudgedDistance.z = 0;

    float  d              = dot(-fudgedDistance, shadedPixelNormal);
    // HACK: We allow the light to be somewhat behind the surface without occluding it,
    //  and we want a smooth ramp between occluded and not-occluded
    float  normalFactor   = clamp((d + DOT_OFFSET) / DOT_RAMP_RANGE, 0, 1);
    return normalFactor * distanceFactor;
}
