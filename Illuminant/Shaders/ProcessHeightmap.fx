#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"

uniform float3 TapSpacingAndBias;
uniform float2 DisplacementScale;
uniform bool NormalsAreSigned;

sampler HeightmapSampler {
    Texture = (BitmapTexture);
    MipFilter = LINEAR;
    MinFilter = LINEAR;
    MagFilter = LINEAR;
    AddressU = CLAMP;
    AddressV = CLAMP; 
};

float tap (
    float2 uv,
    float4 texRgn,
    out float alpha
) {
    float4 rgba = tex2Dbias(HeightmapSampler, float4(clamp(uv, texRgn.xy, texRgn.zw), 0, TapSpacingAndBias.z));
    float luminance;
    ExtractLuminanceAlpha(rgba, BitmapTraits, luminance, alpha);
    return luminance;
}

float synthesizeAlpha (float value) {
    return smoothstep(0.005, 0.15, abs(value));
}

float3 calculateNormal (
    float2 texCoord, float4 texRgn, out float alpha
) {
    float3 spacing = float3(TapSpacingAndBias.xy, 0);
    if (spacing.x <= 0)
        spacing = float3(HalfTexel, 0);
    float epsilon = 0.001, temp;

    float a = tap(texCoord - spacing.xz, texRgn, temp), b = tap(texCoord + spacing.xz, texRgn, temp),
        c = tap(texCoord - spacing.zy, texRgn, temp), d = tap(texCoord + spacing.zy, texRgn, temp),
        center = tap(texCoord, texRgn, alpha);

    // If the current pixel is entirely influenced by heightmap values that are nearly zero, we should
    //  give it a low alpha value so that when refraction shaders consume it they can avoid performing
    //  mip bias for this pixel. Without doing this, heightmap=0 pixels will be blurry when mip bias
    //  is enabled, and that isn't what we want.
    // This alpha value isn't used to govern refraction itself (if it was, this would produce weird
    //  hard edges or other artifacts.)
    alpha = max(
        synthesizeAlpha(center), 
        max(
            synthesizeAlpha(a), max(
                synthesizeAlpha(b), max(
                    synthesizeAlpha(c), 
                    synthesizeAlpha(d)
                )
            )
        )
    );

    if (
        (abs(center) < epsilon) && (abs(a) < epsilon) &&
        (abs(b) < epsilon) && (abs(c) < epsilon) &&
        (abs(d) < epsilon)
    )
        alpha = 0;

    return normalize(float3(
        a - b,
        c - d,
        // Normally if we were sampling a 3d space, we'd be subtracting two taps here.
        // But we're sampling a 2d space so the delta of the taps would always be zero.
        // We use the height value instead so that there's an observable difference between
        //  non-zero heights and zero heights.
        0.5
    ));
}

void HeightmapToNormalsPixelShader (
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result      : COLOR0
) {
    float alpha;
    float3 normal = calculateNormal(texCoord, texRgn, alpha);
    result = float4(NormalsAreSigned ? normal : (normal * 0.5) + 0.5, alpha);
}

void HeightmapToDisplacementPixelShader (
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result      : COLOR0 
) {
    float alpha;
    float3 normal = calculateNormal(texCoord, texRgn, alpha);
    float3 displacement = normal.xyz * float3(DisplacementScale, 1);
    result = float4(displacement.xy + 0.5, 0.5, 1);
}

technique HeightmapToNormals
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HeightmapToNormalsPixelShader();
    }
}

technique HeightmapToDisplacement
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 HeightmapToDisplacementPixelShader();
    }
}