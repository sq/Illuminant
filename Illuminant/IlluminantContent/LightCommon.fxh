// Bias and scale the dot product of the light vector and surface normal
// These values produce a very rapid climb up to 1.0 (we don't really want
//  smooth dot attenuation, we're just using it to mask light from behind
//  surfaces)
#define DOT_OFFSET     0.1
#define DOT_RAMP_RANGE 0.1
// The final output from the dot computation is raised to this power so
#define DOT_EXPONENT   0.85

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
    float  distanceFactor = 1 - clamp((distance - lightRadius) / lightRampLength, 0, 1);

    if (exponential)
        distanceFactor *= distanceFactor;

    float3 lightNormal  = distance3 / distance;

    /*
    float3 crossProduct = cross(shadedPixelNormal, lightNormal);
    float3 lightEdgeA   = lightCenter + (crossProduct * lightRadius);
    float3 lightEdgeB   = lightCenter - (crossProduct * lightRadius);

    // HACK: Because we are modeling a spherical light source instead of a point light, simply
    //  using the dot product of the light vector & surface normal won't be sufficient.
    // We compute the cross product of the light vector & surface normal to get an additional
    //  pair of vectors that we can use to select points on the surface of the light source,
    //  and then also compute dot products for the vector between those additional points and
    //  the shaded point.
    // This (maybe?) approximates whether light from the sphere can reach the point, for our
    //  purposes.
    float  dotA = dot(-normalize(shadedPixelPosition - lightEdgeA), shadedPixelNormal);
    float  dotB = dot(-normalize(shadedPixelPosition - lightEdgeB), shadedPixelNormal);
    float  dotC = dot(-lightNormal, shadedPixelNormal);

    // FIXME: If the light center is inside the surface we get a gross dark blob
    float  d = max(max(dotA, dotB), dotC);
    */

    float d = dot(-lightNormal, shadedPixelNormal);

    // HACK: We allow the light to be somewhat behind the surface without occluding it,
    //  and we want a smooth ramp between occluded and not-occluded
    float  normalFactor = pow(clamp((d + DOT_OFFSET) / DOT_RAMP_RANGE, 0, 1), DOT_EXPONENT);

    return normalFactor * distanceFactor;
}
