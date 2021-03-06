// Results in /Od are completely incorrect
#pragma fxcparams(/O3 /Zi)

#define ENABLE_DISTANCE_FIELD 1

#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "DistanceFieldCommon.fxh"
#include "ConeTrace.fxh"
#include "AOCommon.fxh"
#include "ProjectorLightCore.fxh"

void ProjectorLightPixelShader(
    in  float3 worldPosition       : POSITION1,
    in  float4 mat1                : TEXCOORD0,
    in  float4 mat2                : TEXCOORD1,
    in  float4 mat3                : TEXCOORD4,
    in  float4 mat4                : TEXCOORD5,
    // radius, ramp length, ramp mode, enable shadows
    in  float4 lightProperties     : TEXCOORD2,
    // ao radius, opacity, wrap, ao opacity
    in  float4 moreLightProperties : TEXCOORD3,
    // texX1, texY1, texX2, texY2,
    in  float4 evenMoreLightProperties : TEXCOORD7,
    // x, y, z, hasOrigin
    in  float4 projectorOrigin     : TEXCOORD6,
    in  float  mipBias             : TEXCOORD8,
    ACCEPTS_VPOS,
    out float4 result              : COLOR0
) {
    float3 shadedPixelPosition, shadedPixelNormal;
    bool enableShadows, fullbright;
    sampleGBuffer(
        GET_VPOS,
        shadedPixelPosition, shadedPixelNormal, enableShadows, fullbright
    );

    if (fullbright) {
        result = 0;
        discard;
        return;
    }

    lightProperties.w *= enableShadows;

    float4 projectorSpacePosition;
    float opacity = ProjectorLightPixelCore(
        shadedPixelPosition, shadedPixelNormal,
        mat1, mat2, mat3, mat4,
        lightProperties, moreLightProperties, evenMoreLightProperties,
        projectorOrigin, projectorSpacePosition
    );

    result = ProjectorLightColorCore(projectorSpacePosition, mipBias, opacity);
}

technique ProjectorLight {
    pass P0
    {
        vertexShader = compile vs_3_0 ProjectorLightVertexShader();
        pixelShader  = compile ps_3_0 ProjectorLightPixelShader();
    }
}