#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"
#include "GBufferShaderCommon.fxh"

uniform const bool NormalsAreSigned;
uniform const float2 ViewCoordinateScaleFactor;

void AutoGBufferBitmapPixelShader (
    in float4 color : COLOR0,
    // originX, originY, vertexX, vertexY
    in float4 originalPositionData : TEXCOORD7,
    // normalZ, zToYRatio, z, enableShadows
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    // FIXME: Without this we get weird acne at the top/left edges of billboards.
    texRgn.xy += HalfTexel;
    texRgn.zw += HalfTexel;

    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
    texColor.a *= color.a;

    bool isDead = texColor.a < 0.5;

    float3 normal = (userData.x < -900)
        // Use massively negative Z normal to represent 'I don't want directional light occlusion at all, thanks'
        ? float3(0, 0, 0)
        : float3(0, 1 - abs(userData.x), userData.x);
    float relativeY = (originalPositionData.y - originalPositionData.w) * ViewCoordinateScaleFactor.y;
    float z = userData.z + (userData.y * relativeY);
    result = encodeGBufferSample(
        (normal.xyz - 0.5) * 2, relativeY, z, isDead, 
        // enable shadows
        userData.w > 0.5,
        // fullbright
        (userData.w < -0.5)
    );

    if (isDead)
        discard;
}

void NormalBillboardPixelShader(
    in float4 color : COLOR0,
    // originX, originY, vertexX, vertexY
    in float4 originalPositionData : TEXCOORD7,
    // normalZ, zToYRatio, z, enableShadows
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    // FIXME: Without this we get weird acne at the top/left edges of billboards.
    texRgn.xy += HalfTexel;
    texRgn.zw += HalfTexel;

    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
    texColor.a *= color.a;

    float3 normal = NormalsAreSigned ? texColor.rgb : (texColor.rgb - 0.5) * 2.0;
    bool isDead = texColor.a < 0.5;

    float relativeY = (originalPositionData.y - originalPositionData.w) * ViewCoordinateScaleFactor.y;
    float z = userData.z + (userData.y * relativeY);
    result = encodeGBufferSample(
        normal, relativeY, z, isDead,
        // enable shadows
        userData.w > 0.5,
        // fullbright
        (userData.w < -0.5)
    );

    if (isDead)
        discard;
}

technique AutoGBufferBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 AutoGBufferBitmapPixelShader();
    }
}

technique NormalBillboard
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 NormalBillboardPixelShader();
    }
}