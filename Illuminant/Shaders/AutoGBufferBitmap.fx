#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"
#include "GBufferShaderCommon.fxh"

uniform const bool NormalsAreSigned;
uniform const float2 ViewCoordinateScaleFactor;
// min z offset, max z offset, distance scale, rim light normal width
uniform const float4 ZFromDistance;

void AutoGBufferBitmapPixelShader (
    in float4 color : COLOR0,
    // originX, originY, vertexX, vertexY
    in float4 originalPositionData : TEXCOORD7,
    // normalZ, zToYRatio, z, enableShadows
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    // FIXME: Without this we get weird acne at the top/left edges of billboards.
    texRgn.xy += BitmapTexelSize * 0.5;
    texRgn.zw += BitmapTexelSize * 0.5;

    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
    texColor.a *= color.a;

    bool isDead = texColor.a < 0.5;

    float3 normal = (userData.x < -900)
        // Use massively negative Z normal to represent 'I don't want directional light occlusion at all, thanks'
        ? float3(0, 0, 0)
        : normalize(float3(0, 1 - abs(userData.x), userData.x));
    float relativeY = (originalPositionData.y - originalPositionData.w) * ViewCoordinateScaleFactor.y;
    float z = userData.z + (userData.y * relativeY);

    if (abs(ZFromDistance.z) > 0.001)
    {
        float distance = tex2D(TextureSampler2, clamp2(texCoord2, texRgn2.xy, texRgn2.zw)).r;
        z += clamp(ZFromDistance.z * distance, ZFromDistance.x, ZFromDistance.y);
    }

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

void NormalBillboardPixelShader(
    in float4 color : COLOR0,
    // originX, originY, vertexX, vertexY
    in float4 originalPositionData : TEXCOORD7,
    // normalZ, zToYRatio, z, enableShadows
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    // FIXME: Without this we get weird acne at the top/left edges of billboards.
    texRgn.xy += BitmapTexelSize * 0.5;
    texRgn.zw += BitmapTexelSize * 0.5;

    float4 texColor = tex2D(TextureSampler, clamp2(texCoord, texRgn.xy, texRgn.zw));
    texColor.a *= color.a;

    float3 normal = NormalsAreSigned ? texColor.rgb : (texColor.rgb - 0.5) * 2.0;
    bool isDead = (texColor.a < 0.5) || (length(texColor.rgb) < 0.01);

    float relativeY = (originalPositionData.y - originalPositionData.w) * ViewCoordinateScaleFactor.y;
    float z = userData.z + (userData.y * relativeY);

    if (abs(ZFromDistance.z) > 0.001) {
        float distance = tex2D(TextureSampler2, clamp2(texCoord2, texRgn2.xy, texRgn2.zw)).r;
        z += clamp(ZFromDistance.z * distance, ZFromDistance.x, ZFromDistance.y);
    }

    result = encodeGBufferSample(
        normal, relativeY, z, isDead,
        // enable shadows
        userData.w > 0.5,
        // fullbright
        (userData.w < -0.5)
    );

    if (texColor.a < 0.5)
        discard;
}

float tap(
    float2 uv,
    float4 texRgn
) {
    return tex2Dlod(TextureSampler, float4(clamp(uv, texRgn.xy, texRgn.zw), 0, 0)).r;
}

float3 calculateNormal(
    float2 texCoord, float4 texRgn, float2 texelSize, float4 traits, float z,
    out float center
) {
    float3 spacing = float3(texelSize, 0);
    float epsilon = 0.001, temp;

    float a = tap(texCoord - spacing.xz, texRgn), b = tap(texCoord + spacing.xz, texRgn),
        c = tap(texCoord - spacing.zy, texRgn), d = tap(texCoord + spacing.zy, texRgn);
    
    center = tap(texCoord, texRgn);
    
    return normalize(float3(
        a - b,
        c - d,
        // Normally if we were sampling a 3d space, we'd be subtracting two taps here.
        // But we're sampling a 2d space so the delta of the taps would always be zero.
        // We use a constant instead to get somewhat consistent behavior.
        z
    ));
}

// https://blog.demofox.org/2016/02/19/normalized-vector-interpolation-tldr/
float3 slerp(float3 start, float3 end, float percent) {
     // Dot product - the cosine of the angle between 2 vectors.
    float slerpDot = dot(start, end);
     // Clamp it to be in the range of Acos()
     // This may be unnecessary, but floating point
     // precision can be a fickle mistress.
    slerpDot = clamp(slerpDot, -1.0, 1.0);
     // Acos(dot) returns the angle between start and end,
     // And multiplying that by percent returns the angle between
     // start and the final result.
    float theta = acos(slerpDot) * percent;
    float3 RelativeVec = normalize(end - start * slerpDot); // Orthonormal basis
     // The final result.
    return ((start * cos(theta)) + (RelativeVec * sin(theta)));
}

void DistanceBillboardPixelShader(
    in float4 color : COLOR0,
    // originX, originY, vertexX, vertexY
    in float4 originalPositionData : TEXCOORD7,
    // normalZ, zToYRatio, z, enableShadows
    in float4 userData : COLOR2,
    in float2 texCoord : TEXCOORD0,
    in float4 texRgn : TEXCOORD1,
    in float2 texCoord2 : TEXCOORD2,
    in float4 texRgn2 : TEXCOORD3,
    out float4 result : COLOR0
) {
    // FIXME: Without this we get weird acne at the top/left edges of billboards.
    texRgn.xy += BitmapTexelSize * 0.5;
    texRgn.zw += BitmapTexelSize * 0.5;

    float relativeY = (originalPositionData.y - originalPositionData.w) * ViewCoordinateScaleFactor.y;
    float z = userData.z + (userData.y * relativeY), distance;
    
    // HACK: We negate the result normal since we're sampling a distance field (and a distance field
    //  is negative as you go in, which is "opposite" of how a heightmap works)
    // FIXME: Is 0.5 the right z value?
    float3 normal = calculateNormal(texCoord, texRgn, BitmapTexelSize, BitmapTraits, 0.5, distance) * -1;
    bool isDead = false;

    if (abs(ZFromDistance.z) > 0.001) {
        z += clamp(ZFromDistance.z * distance, ZFromDistance.x, ZFromDistance.y);
    }
    
    if (distance > 0)
        isDead = true;
    
    normal = normalize(normal);
    normal = slerp(normal, float3(0, 0, 1), 1 - smoothstep(-ZFromDistance.w, 0, distance));

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

technique DistanceBillboard {
    pass P0 {
        vertexShader = compile vs_3_0 GenericVertexShader();
        pixelShader = compile ps_3_0 DistanceBillboardPixelShader();
    }
}