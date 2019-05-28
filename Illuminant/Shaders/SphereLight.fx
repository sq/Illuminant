#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "SphereLightCore.fxh"

float3 GetVertexForIndex (int index) {
    DEFINE_ClippedLightVertices
    return ClippedLightVertices[index];
    /*
    switch (abs(index)) {
        case 0:
            return float3(cOne, 0, 0);
        case 1:
            return float3(mOne, 0, 0);
        case 2:
            return float3(mOne, 1, 0);
        case 3:
            return float3(cOne, 1, 0);
        case 4:
            return float3(mOne, cOne, 0);
        case 5:
            return float3(1, cOne, 0);
        case 6:
            return float3(1, mOne, 0);
        case 7:
            return float3(mOne, mOne, 0);
        case 8:
            return float3(0, cOne, 0);
        case 9:
            return float3(cOne, cOne, 0);
        case 10:
            return float3(cOne, mOne, 0);
        case 11:
            return float3(0, mOne, 0);
    }
    return 0;
    */
}

void SphereLightVertexShader(
    in int2 vertexIndex              : BLENDINDICES0,
    inout float3 lightCenter         : TEXCOORD0,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 color               : TEXCOORD4,
    out float3 worldPosition         : POSITION1,
    out float4 result                : POSITION0
) {
    float3 vertex = GetVertexForIndex(vertexIndex.x);

    float  radius = lightProperties.x + lightProperties.y + 1;
    float  deltaY = (radius)-(radius / moreLightProperties.z);
    float3 radius3;
    if (1)
        // HACK: Scale the y axis some to clip off dead pixels caused by the y falloff factor
        radius3 = float3(radius, radius - (deltaY / 2.0), 0);
    else
        radius3 = float3(radius, radius, 0);

    float3 tl = lightCenter - radius3, br = lightCenter + radius3;
    worldPosition = lerp(tl, br, vertex);

    // Unfortunately we need to adjust both by the light's radius (to account for pixels above/below the center point
    //  being lit in 2.5d projection), along with adjusting by the z of the light's centerpoint (to deal with pixels
    //  at high elevation)
    float radiusOffset = radius * getInvZToYMultiplier();
    float zOffset = lightCenter.z * getZToYMultiplier();

    if (vertex.y < 0.5) {
        worldPosition.y -= radiusOffset;
        worldPosition.y -= zOffset;
    }

    // FIXME
    if (1) {
        result = float4(vertex.x * 2 - 1, vertex.y * 2 - 1, 0, 1);
        return;
    }

    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

void SphereLightPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    if (0) {
        result = float4(color.rgb, 1);
        return;
    }

    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    if (1) {
        // FIXME: GET_VPOS is right but shadedPixelPosition.y is wrong
        float2 rtDims = GetRenderTargetSize();
        result = float4(shadedPixelPosition.x / rtDims.x, 0, shadedPixelPosition.y / rtDims.y, 1);
        return;
    }

    float opacity;
    if (1) {
        opacity = computeSphereLightOpacity(
            shadedPixelPosition, shadedPixelNormal,
            lightCenter, lightProperties, moreLightProperties.z
        );
    } else {
        opacity = SphereLightPixelCore(
            shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, false
        );
    }

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightWithDistanceRampPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, true, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void SphereLightWithOpacityRampPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 lightCenter         : TEXCOORD0,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 color               : TEXCOORD4,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal
    );

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, true
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