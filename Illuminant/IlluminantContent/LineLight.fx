#include "SphereLightCore.fxh"

void LineLightVertexShader(
    in int2 vertexIndex              : BLENDINDICES0,
    inout float3 startPosition       : TEXCOORD0,
    inout float3 endPosition         : TEXCOORD1,
    // radius, ramp length, ramp mode, enable shadows
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, distance falloff, y falloff factor, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 startColor          : TEXCOORD4,
    inout float4 endColor            : TEXCOORD5,
    out float3 worldPosition         : POSITION1,
    out float4 result                : POSITION0
) {
    float3 vertex = LightCorners[vertexIndex.x];

    float  radius = lightProperties.x + lightProperties.y + 1;
    float  deltaY = (radius) - (radius / moreLightProperties.z);
    float3 radius3;

    if (1)
        // HACK: Scale the y axis some to clip off dead pixels caused by the y falloff factor
        radius3 = float3(radius, radius - (deltaY / 2.0), 0);
    else
        radius3 = float3(radius, radius, 0);

    float3 p1 = min(startPosition, endPosition), p2 = max(startPosition, endPosition);
    float3 tl = p1 - radius3, br = p2 + radius3;

    // Unfortunately we need to adjust both by the light's radius (to account for pixels above/below the center point
    //  being lit in 2.5d projection), along with adjusting by the z of the light's centerpoint (to deal with pixels
    //  at high elevation)
    float radiusOffset = radius * getInvZToYMultiplier();
    // FIXME
    float effectiveZ = startPosition.z;
    float zOffset = effectiveZ * getZToYMultiplier();

    worldPosition = lerp(tl, br, vertex);

    if (vertex.y < 0.5) {
        worldPosition.y -= radiusOffset;
        worldPosition.y -= zOffset;
    }

    float3 screenPosition = (worldPosition - float3(Viewport.Position.xy, 0));
    screenPosition.xy *= Viewport.Scale * Environment.RenderScale;
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float3 computeLightCenter (float3 worldPosition, float3 startPosition, float3 endPosition, out float u, inout float4 lightProperties) {
    float2 xy = closestPointOnLine(worldPosition.xy, startPosition.xy, endPosition.xy, u);
    float z = lerp(startPosition.z, endPosition.z, u);
    float3 result = float3(xy, z);
    float distanceFromEndpoint = 1 - (abs(u - 0.5) * 2);
    float maxOffsetDistance = min(length(endPosition - startPosition), 128);
    float offsetDistance = maxOffsetDistance * distanceFromEndpoint;
    // lightProperties.x = max(lightProperties.x + offsetDistance, 1);
    // result -= offsetDistance * normalize(result - worldPosition);
    return result;
}

void LineLightPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 startPosition       : TEXCOORD0,
    in  float3 endPosition         : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 startColor          : TEXCOORD4,
    in  float4 endColor            : TEXCOORD5,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float u;
    float3 lightCenter = computeLightCenter(worldPosition, startPosition, endPosition, u, lightProperties);
    float4 color = lerp(startColor, endColor, u);

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void LineLightWithDistanceRampPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 startPosition       : TEXCOORD0,
    in  float3 endPosition         : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 startColor          : TEXCOORD4,
    in  float4 endColor            : TEXCOORD5,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float u;
    float3 lightCenter = computeLightCenter(worldPosition, startPosition, endPosition, u, lightProperties);
    float4 color = lerp(startColor, endColor, u);

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, true, false
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

void LineLightWithOpacityRampPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float3 startPosition       : TEXCOORD0,
    in  float3 endPosition         : TEXCOORD1,
    in  float4 lightProperties     : TEXCOORD2,
    in  float4 moreLightProperties : TEXCOORD3,
    in  float4 startColor          : TEXCOORD4,
    in  float4 endColor            : TEXCOORD5,
    in  float2 vpos                : VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition;
    float3 shadedPixelNormal;
    sampleGBuffer(
        vpos,
        shadedPixelPosition, shadedPixelNormal
    );

    float u;
    float3 lightCenter = computeLightCenter(worldPosition, startPosition, endPosition, u, lightProperties);
    float4 color = lerp(startColor, endColor, u);

    float opacity = SphereLightPixelCore(
        shadedPixelPosition, shadedPixelNormal, lightCenter, lightProperties, moreLightProperties, false, true
    );

    result = float4(color.rgb * color.a * opacity, 1);
}

technique LineLight {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightVertexShader();
        pixelShader  = compile ps_3_0 LineLightPixelShader();
    }
}

technique LineLightWithDistanceRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightVertexShader();
        pixelShader  = compile ps_3_0 LineLightWithDistanceRampPixelShader();
    }
}

technique LineLightWithOpacityRamp {
    pass P0
    {
        vertexShader = compile vs_3_0 LineLightVertexShader();
        pixelShader  = compile ps_3_0 LineLightWithOpacityRampPixelShader();
    }
}