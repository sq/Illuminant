// Results in /Od are sometimes incorrect
#pragma fxcparams(/O3 /Zi)

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\DitherCommon.fxh"
#include "SphereLightCore.fxh"
#include "Bezier.fxh"
#include "ParticleCommon.fxh"

uniform float4 LightProperties;
uniform float4 MoreLightProperties;
uniform float4 LightColor;

void ParticleLightVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    in float2 xy                     : POSITION0,
    in float3 offsetAndIndex         : POSITION1,
    out float4 lightCenter           : TEXCOORD0,
    out float3 worldPosition         : TEXCOORD1,
    out float4 lightProperties       : TEXCOORD2,
    out float4 moreLightProperties   : TEXCOORD3,
    out float4 lightColor            : COLOR0,
    out float4 result                : POSITION0
) {
    if (StippleReject (offsetAndIndex.z, StippleFactor)) {
        result = float4(0, 0, 0, 0);
        lightColor = float4(0, 0, 0, 0);
        return;
    }

    DEFINE_LightCorners
    float3 corner = LightCorners[cornerIndex.x];

    float4 actualXy = float4(xy + offsetAndIndex.xy, 0, 0);
    float4 position, renderData, renderColor;
    // readStateUv(actualXy, position, renderData, renderColor);
    position = tex2Dlod(PositionSampler, actualXy);
    renderData = 0;
    renderColor = tex2Dlod(AttributeSampler, actualXy);
    // Unpremultiply
    if (renderColor.a > 0)
        renderColor.rgb /= renderColor.a;

    // HACK
    float life = position.w;
    if (life <= 0) {
        result = float4(0, 0, 0, 0);
        lightColor = float4(0, 0, 0, 0);
        return;
    }

    lightCenter = float4(position.xyz, 0);
    float  radius = LightProperties.x + LightProperties.y + 1;
    float3 radius3 = float3(radius, radius, 0);
    float3 tl = lightCenter - radius3, br = lightCenter + radius3;

    // Unfortunately we need to adjust both by the light's radius (to account for pixels above/below the center point
    //  being lit in 2.5d projection), along with adjusting by the z of the light's centerpoint (to deal with pixels
    //  at high elevation)
    tl.y -= radius * getInvZToYMultiplier();
    tl.y -= lightCenter.z * getZToYMultiplier();

    worldPosition = lerp(tl, br, corner);

    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);

    lightColor = renderColor * LightColor;

    lightProperties = LightProperties;
    moreLightProperties = MoreLightProperties;

    if (lightColor.a <= 0) {
        result = float4(0, 0, 0, 0);
        lightColor = float4(0, 0, 0, 0);
    }
}

void ParticleLightPixelShader(
    in  float4 lightCenter         : TEXCOORD0,
    in  float3 worldPosition       : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 lightColor          : COLOR0,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition, shadedPixelNormal;
    bool enableShadows;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, enableShadows
    );

    lightProperties.w *= enableShadows;

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter.xyz, lightProperties, moreLightProperties
    );

    result = float4(lightColor.rgb * lightColor.a * opacity, 1);
}

void ParticleLightWithoutDistanceFieldPixelShader(
    in  float4 lightCenter         : TEXCOORD0,
    in  float3 worldPosition       : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 lightColor : COLOR0,
    ACCEPTS_VPOS,
    out float4 result : COLOR0
) {
    float3 shadedPixelPosition, shadedPixelNormal;
    bool enableShadows;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, enableShadows
    );

    lightProperties.w *= enableShadows;

    float opacity = SphereLightPixelCoreNoDF(
        shadedPixelPosition, shadedPixelNormal, lightCenter.xyz, lightProperties, moreLightProperties
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

technique ParticleLightWithoutDistanceField {
    pass P0
    {
        vertexShader = compile vs_3_0 ParticleLightVertexShader();
        pixelShader = compile ps_3_0 ParticleLightWithoutDistanceFieldPixelShader();
    }
}