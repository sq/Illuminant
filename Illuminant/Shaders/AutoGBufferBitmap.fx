#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"
#include "GBufferShaderCommon.fxh"

void AutoGBufferBitmapPixelShader (
    in float4 normal : COLOR0,
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    out float4 result : COLOR0
) {
    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));

    bool isDead = texColor.a < 0.5;
    float relativeY = 0, z = 0;
    result = encodeGBufferSample(
        (normal.xyz - 0.5) * 2, relativeY, z, isDead
    );

    if (isDead)
        discard;

    /*
    float dataScale = dataScaleAndDynamicFlag.x;
    float yOffset = data.b * dataScale;
    float effectiveZ = worldPosition.z + (yOffset * getInvZToYMultiplier());

    // HACK: We drop the world x axis and the normal y axis,
    //  and reconstruct those two values when sampling the g-buffer
    result = float4(
        data.r,
        data.g,
        yOffset / RELATIVEY_SCALE,
        (effectiveZ / 512) * dataScaleAndDynamicFlag.y
    );

    const float discardThreshold = (1.0 / 255.0);
    clip(texColor.a - discardThreshold);
    */
}

technique AutoGBufferBitmap
{
    pass P0
    {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 AutoGBufferBitmapPixelShader();
    }
}