#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "LightCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "RampCommon.fxh"
#include "AOCommon.fxh"

#define DEBUG_COORDS 0
#define ALLOW_DISCARD 1
#define BOUNDING_BOX 1
#define SELF_OCCLUSION_HACK 1.5
#define SHADOW_OPACITY_THRESHOLD (0.75 / 255.0)
#define PROJECTOR_FILTERING LINEAR

sampler ProjectorTextureSampler : register(s5) {
    Texture = (RampTexture);
    AddressU = WRAP;
    AddressV = WRAP;
    MipFilter = PROJECTOR_FILTERING;
    MinFilter = PROJECTOR_FILTERING;
    MagFilter = PROJECTOR_FILTERING;
};

float ProjectorLightPixelCore(
    in float3 shadedPixelPosition,
    in float3 shadedPixelNormal,
    in float4 mat1, 
    in float4 mat2, 
    in float4 mat3, 
    in float4 mat4,
    // opacity, wrap, texX1, texY1
    in float4 lightProperties,
    // ao radius, texX2, texY2, ao opacity
    in float4 moreLightProperties,
    in float4 projectorOrigin,
    out float4 projectorSpacePosition
) {
    float4 coneLightProperties = lightProperties;

    float4x4 invMatrix = float4x4(
        mat1, mat2, mat3, mat4
    );

    // Map into projector space so the xy are texture coordinates
    projectorSpacePosition = mul(float4(shadedPixelPosition, 1), invMatrix);
    projectorSpacePosition /= projectorSpacePosition.w;
    // Offset into texture region
    projectorSpacePosition.xy += lightProperties.zw;

    // HACK: If the projector space position drops below 0 on the z axis just force it up to 0 since the light would hit
    //  the ground
    // Disabling this allows a projection to be elevated off the ground but breaks slanted rotation because some of the 
    //  projected points end up below 0
    projectorSpacePosition.z = max(0, projectorSpacePosition.z);

    coneLightProperties.z = 0;
    coneLightProperties.w = 0;

    float constantOpacity = lightProperties.x;

    float distanceOpacity = 1;
    float3 clampedPosition = clamp3(projectorSpacePosition, float3(lightProperties.zw, 0), float3(moreLightProperties.yz, 1));

    // If lamp is clamped, apply distance falloff
    if (ALLOW_DISCARD) {
        float2 sz = moreLightProperties.yz - lightProperties.zw;
        float threshold = 0.001;
        float distanceToVolume = min(length(clampedPosition - projectorSpacePosition), threshold) * (1 / threshold);

        if (lightProperties.y > 0.5)
            distanceOpacity = max(1 - distanceToVolume, 0);
    }

    bool visible = (distanceOpacity > 0) && 
        (shadedPixelPosition.x > -9999) &&
        (constantOpacity > 0);

    clip(visible ? 1 : -1);

    // Optionally clamp to texture region
    projectorSpacePosition.xy = lerp(projectorSpacePosition.xy, clampedPosition.xy, lightProperties.y);

    // Zero out y/z before we pass them into AO
    moreLightProperties.y = 0;
    moreLightProperties.z = 0;

    DistanceFieldConstants vars = makeDistanceFieldConstants();

    // HACK: AO is only on upward-facing surfaces
    moreLightProperties.x *= max(0, shadedPixelNormal.z);

    float aoOpacity = computeAO(shadedPixelPosition, shadedPixelNormal, moreLightProperties, vars, visible);

    float3 lightNormal = normalize(shadedPixelPosition - projectorOrigin.xyz);
    float normalOpacity = lerp(1, computeNormalFactor(lightNormal, shadedPixelNormal), projectorOrigin.w);

    float preTraceOpacity = distanceOpacity * aoOpacity * normalOpacity;

    // FIXME: Projector shadows?
    /*
    bool traceShadows = visible && lightProperties.w && (preTraceOpacity >= SHADOW_OPACITY_THRESHOLD);
    float coneOpacity = lineConeTrace(
        startPosition, endPosition, u,
        coneLightProperties.xy, 
        float2(getConeGrowthFactor(), moreLightProperties.y),
        shadedPixelPosition + (SELF_OCCLUSION_HACK * shadedPixelNormal),
        vars, traceShadows
    );
    */
    float coneOpacity = 1;

    float lightOpacity = preTraceOpacity;
    lightOpacity *= coneOpacity;
    lightOpacity *= constantOpacity;

    // HACK: Don't cull pixels unless they were killed by distance falloff.
    // This ensures that billboards are always lit.
    clip(visible ? 1 : -1);
    return visible ? lightOpacity : 0;
}

float4x4 invertMatrix (float4x4 m) {
    float n11 = m[0][0], n12 = m[1][0], n13 = m[2][0], n14 = m[3][0];
    float n21 = m[0][1], n22 = m[1][1], n23 = m[2][1], n24 = m[3][1];
    float n31 = m[0][2], n32 = m[1][2], n33 = m[2][2], n34 = m[3][2];
    float n41 = m[0][3], n42 = m[1][3], n43 = m[2][3], n44 = m[3][3];

    float t11 = n23 * n34 * n42 - n24 * n33 * n42 + n24 * n32 * n43 - n22 * n34 * n43 - n23 * n32 * n44 + n22 * n33 * n44;
    float t12 = n14 * n33 * n42 - n13 * n34 * n42 - n14 * n32 * n43 + n12 * n34 * n43 + n13 * n32 * n44 - n12 * n33 * n44;
    float t13 = n13 * n24 * n42 - n14 * n23 * n42 + n14 * n22 * n43 - n12 * n24 * n43 - n13 * n22 * n44 + n12 * n23 * n44;
    float t14 = n14 * n23 * n32 - n13 * n24 * n32 - n14 * n22 * n33 + n12 * n24 * n33 + n13 * n22 * n34 - n12 * n23 * n34;

    float det = n11 * t11 + n21 * t12 + n31 * t13 + n41 * t14;
    float idet = 1.0f / det;

    float4x4 ret;

    ret[0][0] = t11 * idet;
    ret[0][1] = (n24 * n33 * n41 - n23 * n34 * n41 - n24 * n31 * n43 + n21 * n34 * n43 + n23 * n31 * n44 - n21 * n33 * n44) * idet;
    ret[0][2] = (n22 * n34 * n41 - n24 * n32 * n41 + n24 * n31 * n42 - n21 * n34 * n42 - n22 * n31 * n44 + n21 * n32 * n44) * idet;
    ret[0][3] = (n23 * n32 * n41 - n22 * n33 * n41 - n23 * n31 * n42 + n21 * n33 * n42 + n22 * n31 * n43 - n21 * n32 * n43) * idet;

    ret[1][0] = t12 * idet;
    ret[1][1] = (n13 * n34 * n41 - n14 * n33 * n41 + n14 * n31 * n43 - n11 * n34 * n43 - n13 * n31 * n44 + n11 * n33 * n44) * idet;
    ret[1][2] = (n14 * n32 * n41 - n12 * n34 * n41 - n14 * n31 * n42 + n11 * n34 * n42 + n12 * n31 * n44 - n11 * n32 * n44) * idet;
    ret[1][3] = (n12 * n33 * n41 - n13 * n32 * n41 + n13 * n31 * n42 - n11 * n33 * n42 - n12 * n31 * n43 + n11 * n32 * n43) * idet;

    ret[2][0] = t13 * idet;
    ret[2][1] = (n14 * n23 * n41 - n13 * n24 * n41 - n14 * n21 * n43 + n11 * n24 * n43 + n13 * n21 * n44 - n11 * n23 * n44) * idet;
    ret[2][2] = (n12 * n24 * n41 - n14 * n22 * n41 + n14 * n21 * n42 - n11 * n24 * n42 - n12 * n21 * n44 + n11 * n22 * n44) * idet;
    ret[2][3] = (n13 * n22 * n41 - n12 * n23 * n41 - n13 * n21 * n42 + n11 * n23 * n42 + n12 * n21 * n43 - n11 * n22 * n43) * idet;

    ret[3][0] = t14 * idet;
    ret[3][1] = (n13 * n24 * n31 - n14 * n23 * n31 + n14 * n21 * n33 - n11 * n24 * n33 - n13 * n21 * n34 + n11 * n23 * n34) * idet;
    ret[3][2] = (n14 * n22 * n31 - n12 * n24 * n31 - n14 * n21 * n32 + n11 * n24 * n32 + n12 * n21 * n34 - n11 * n22 * n34) * idet;
    ret[3][3] = (n12 * n23 * n31 - n13 * n22 * n31 + n13 * n21 * n32 - n11 * n23 * n32 - n12 * n21 * n33 + n11 * n22 * n33) * idet;

    return ret;
}

float3 projectBoundingBoxEdge (
    in float4x4 projectorSpaceToWorldSpace,
    in float3 corner, in float3 interpCorner
) {
    // Project the corners of the bounding box through the transformation matrix
    //  at the edges of the Z volume (0 and max) to estimate the maximum
    //  possible bounding box since some transforms will cause a skew or other weird
    //  transformation that doesn't behave as expected if you just project at one Z
    float4 transformed = mul(
        float4(interpCorner, 1), 
        projectorSpaceToWorldSpace
    );
    float3 worldPosition1 = transformed.xyz / transformed.w;

    interpCorner.z = 1;
    transformed = mul(
        float4(interpCorner, 1), 
        projectorSpaceToWorldSpace
    );
    float3 worldPosition2 = transformed.xyz / transformed.w;

    // Select the min/max of the two projected points depending on where our current
    //  corner lies
    return float3(
        lerp(min(worldPosition1.xy, worldPosition2.xy), max(worldPosition1.xy, worldPosition2.xy), corner.xy),
        0
    );
}

void ProjectorLightVertexShader (
    in int2 vertexIndex              : BLENDINDICES0,
    inout float4 mat1                : TEXCOORD0, 
    inout float4 mat2                : TEXCOORD1, 
    inout float4 mat3                : TEXCOORD4, 
    // HACK: mip bias in w, w is always 1
    inout float4 mat4                : TEXCOORD5,
    // opacity, wrap, texX1, texY1
    inout float4 lightProperties     : TEXCOORD2,
    // ao radius, texX2, texY2, ao opacity
    inout float4 moreLightProperties : TEXCOORD3,
    inout float4 origin              : TEXCOORD6,
    out float  mipBias               : TEXCOORD7,
    out float3 worldPosition         : POSITION1,
    out float4 result                : POSITION0
) {
    DEFINE_LightCorners

    mipBias = mat4.w;
    mat4.w = 1;

    float4x4 invMatrix = float4x4(
        mat1, mat2, mat3, mat4
    );

    if ((lightProperties.y > 0.5) && BOUNDING_BOX) {
        float4x4 projectorSpaceToWorldSpace = invertMatrix(invMatrix);
        float2 tl = lightProperties.zw, br = moreLightProperties.yz;

        // HACK: Project each corner of the bounding cube of the projector, then create a 2d
        //  x/y bounding box that encloses all those corners' projected points
        // This ensures we generate a nice uniform quad that definitely covers the whole projection
        // We could just pass the projected points directly through but this interacts poorly with
        //  rotation because we end up sort of rotating everything twice. Oops.
        float2 bboxTl = 999999, bboxBr = -999999;
        float3 corner;

        for (int i = 0; i < 4; i++) {
            corner = LightCorners[i];
            float3 interpCorner = float3(lerp(0, br - tl, corner.xy), 0);
            float3 proj = projectBoundingBoxEdge(projectorSpaceToWorldSpace, corner, interpCorner);
            bboxTl = min(bboxTl, proj.xy);
            bboxBr = max(bboxBr, proj.xy);
        }

        corner = LightCorners[vertexIndex.x];
        worldPosition.xy = lerp(bboxTl, bboxBr, corner.xy);
        worldPosition.z = 0;

        // Pad the bounding box at top and bottom to compensate for any 2.5D effect
        float zOffset = getMaximumZ() * getZToYMultiplier();
        worldPosition.y += (zOffset * ((corner.y * 2) - 1));
    } else {
        float3 tl = -9999, br = 9999;
        float3 vertex = LightCorners[vertexIndex.x];
        worldPosition = lerp(tl, br, vertex);
    }

    float3 screenPosition = (worldPosition - float3(GetViewportPosition(), 0));
    screenPosition.xy *= GetViewportScale() * getEnvironmentRenderScale();
    float4 transformedPosition = mul(mul(float4(screenPosition.xyz, 1), Viewport.ModelView), Viewport.Projection);
    result = float4(transformedPosition.xy, 0, transformedPosition.w);
}

float4 ProjectorLightColorCore (
    float4 projectorSpacePosition,
    float mip, float opacity
) {
    if (DEBUG_COORDS)
        return float4(clamp(projectorSpacePosition.xyz, 0, 1), 1) * opacity;

    projectorSpacePosition.z = 0;
    projectorSpacePosition.w = mip;
    float4 texColor = tex2Dlod(ProjectorTextureSampler, projectorSpacePosition);

    return float4(texColor.rgb * texColor.a * opacity, 1);
}