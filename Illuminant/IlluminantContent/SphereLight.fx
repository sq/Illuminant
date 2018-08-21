#include "SphereLightCore.fxh"

void SphereLightVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float4 color               : TEXCOORD4,
    inout float3 lightCenter         : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float4 moreLightProperties : TEXCOORD3,
    out float3 worldPosition         : TEXCOORD2,
    out float4 result                : POSITION0
) {
    float3 corner = LightCorners[cornerIndex.x];

    float  radius = lightProperties.x + lightProperties.y + 1;
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
}

void SphereLightProbeVertexShader(
    in int2 cornerIndex              : BLENDINDICES0,
    inout float4 color               : TEXCOORD4,
    inout float3 lightCenter         : TEXCOORD0,
    inout float4 lightProperties     : TEXCOORD1,
    inout float4 moreLightProperties : TEXCOORD3,
    out float4 result                : POSITION0
) {
    float2 clipPosition = LightCorners[cornerIndex.x] * 99999;

    result = float4(clipPosition.xy, 0, 1);
}

void SphereLightPixelShader(
    in  float3 worldPosition       : TEXCOORD2,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    bool staticShadowsFlag;
    sampleGBufferEx(
        vpos,
        shadedPixelPosition, shadedPixelNormal,
        staticShadowsFlag
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, false, staticShadowsFlag
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightWithDistanceRampPixelShader(
    in  float3 worldPosition       : TEXCOORD2,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    bool staticShadowsFlag;
    sampleGBufferEx(
        vpos,
        shadedPixelPosition, shadedPixelNormal,
        staticShadowsFlag
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, true, false, staticShadowsFlag
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightWithOpacityRampPixelShader(
    in  float3 worldPosition       : TEXCOORD2,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    bool staticShadowsFlag;
    sampleGBufferEx(
        vpos,
        shadedPixelPosition, shadedPixelNormal,
        staticShadowsFlag
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, true, staticShadowsFlag
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightProbePixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, false, false, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightProbeWithDistanceRampPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, true, false, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightProbeWithOpacityRampPixelShader(
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD1,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float4 shadedPixelNormal;
    float opacity;

    sampleLightProbeBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal, opacity
    );

    moreLightProperties.x = moreLightProperties.w = 0;

    opacity *= SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal.xyz, lightCenter, lightProperties, moreLightProperties, false, true, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

technique SphereLight {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightPixelShader();
    }
}

technique SphereLightWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader  = compile ps_3_0 SphereLightWithDistanceRampPixelShader();
    }
}

technique SphereLightWithOpacityRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightVertexShader();
        pixelShader = compile ps_3_0 SphereLightWithOpacityRampPixelShader();
    }
}

technique SphereLightProbe {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbePixelShader();
    }
}

technique SphereLightProbeWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbeWithDistanceRampPixelShader();
    }
}

technique SphereLightProbeWithOpacityRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 SphereLightProbeVertexShader();
        pixelShader = compile ps_3_0 SphereLightProbeWithOpacityRampPixelShader();
    }
}