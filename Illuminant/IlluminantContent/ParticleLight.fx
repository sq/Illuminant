#include "SphereLightCore.fxh"
#include "ParticleCommon.fxh"

uniform float OpacityFromLife;
uniform float4 LightProperties;
uniform float4 MoreLightProperties;
uniform float4 LightColor;

void ParticleLightVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    in float2 xy                     : POSITION0,
    in float2 offset                 : POSITION1,
    out float3 lightCenter           : TEXCOORD0,
    out float3 worldPosition         : TEXCOORD1,
    out float4 lightProperties       : TEXCOORD2,
    out float4 moreLightProperties   : TEXCOORD3,
    out float4 lightColor            : COLOR0,
    out float4 result                : POSITION0
) {
    float3 corner = LightCorners[cornerIndex.x];

    float2 actualXy = xy + offset;
    float4 position, velocity, attributes;
    readState(actualXy, position, velocity, attributes);

    // HACK
    float life = position.w;
    if (life <= 0) {
        result = float4(0, 0, 0, 0);
        lightColor = float4(0, 0, 0, 0);
        return;
    }

    lightCenter = position.xyz;
    float  radius = LightProperties.x + LightProperties.y + 1;
    float3 radius3 = float3(radius, radius, 0);
    float3 tl = lightCenter - radius3, br = lightCenter + radius3;

    // Unfortunately we need to adjust both by the light's radius (to account for pixels above/below the center point
    //  being lit in 2.5d projection), along with adjusting by the z of the light's centerpoint (to deal with pixels
    //  at high elevation)
    tl.y -= radius * getInvZToYMultiplier();
    tl.y -= lightCenter.z * getZToYMultiplier();

    worldPosition = lerp(tl, br, corner);

    float3 screenPosition = (worldPosition - float3(Viewport.Position.xy, 0));
    screenPosition.xy *= Viewport.Scale * Environment.RenderScale;
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);

    lightColor = attributes * LightColor;

    if (OpacityFromLife > 0)
        lightColor *= clamp(position.w / OpacityFromLife, 0, 1);
    else if (OpacityFromLife < 0)
        lightColor *= 1 - clamp(position.w / -OpacityFromLife, 0, 1);

    lightProperties = LightProperties;
    moreLightProperties = MoreLightProperties;

    if (lightColor.a <= 0) {
        result = float4(0, 0, 0, 0);
        lightColor = float4(0, 0, 0, 0);
    }
}

void ParticleLightPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float3 worldPosition       : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 lightColor          : COLOR0,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, false
    );

    result = float4(lightColor.rgb * lightColor.a * opacity, 1);
}

technique ParticleLight {
    pass P0
    {
        vertexShader = compile vs_3_0 ParticleLightVertexShader();
        pixelShader  = compile ps_3_0 ParticleLightPixelShader();
    }
}