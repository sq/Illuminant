#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"

#define BLUR 0
#define GENERATE_DEAD_PIXELS 1

Texture2D // LeftTexture : register(t0),
    // RightTexture : register(t1),
    AboveTexture : register(t2),
    BelowTexture : register(t3);

sampler LeftSampler {
    Texture = (BitmapTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

sampler RightSampler {
    Texture = (SecondTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

sampler AboveSampler {
    Texture = (AboveTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

sampler BelowSampler {
    Texture = (BelowTexture);
    MipFilter = POINT;
    MinFilter = POINT;
    MagFilter = POINT;
    AddressU = CLAMP;
    AddressV = CLAMP;
};

// If set, the input textures are treated as shadow maps instead of light maps,
//  where dark values are shadow and everything else is full lit
uniform const bool ShadowsOnly;
uniform const int  InputCount;
// Min input value, max input value, z magnitude, inclination
uniform const float4 Configuration1;

float cleanInput (float value) {
    float result = (value - Configuration1.x) / (Configuration1.y - Configuration1.x);
    if (ShadowsOnly)
        result -= 0.5f;
    return saturate(result);
}

float3 tap (in float2 texCoord, in float4 texRgn) {
    float4 uv = float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, 0);
    float4 leftSample = tex2Dlod(LeftSampler, uv);
    // HACK: if the left sample is transparent, generate a dead transparent pixel
    if (leftSample.a <= 0.01)
        return -999;

    float left = cleanInput(leftSample),
        right = InputCount > 1 ? cleanInput(tex2Dlod(RightSampler, uv)) : 1.0 - left,
        above = InputCount > 2 
            ? cleanInput(tex2Dlod(AboveSampler, uv)) 
            : 0.0,
        below = InputCount > 3 
            ? cleanInput(tex2Dlod(BelowSampler, uv)) 
            : (
                (left == right) && (right == above)
                    ? above
                    : 1.0 - above
            ),
        xDelta = right - left,
        yDelta = below - above,
        xyLength = length(float2(xDelta, yDelta)),
        forward = xyLength <= 0.01
            ? 1.0
            : (xyLength >= 0.98
                ? 0.0
                : sqrt(1.0 - xyLength)
                ) * Configuration1.z;

    // HACK: If all the light samples are dark, generate a dead opaque pixel
    if (GENERATE_DEAD_PIXELS) {
        if ((left <= 0.01) && (right <= 0.01) && (above <= 0.01) && (below <= 0.01))
            return 999;
    }

    float3 n = float3(xDelta, yDelta, forward + Configuration1.w);
    return normalize(n);
}

void conditionalTap (inout float3 accum, inout float count, in float2 texCoord, in float4 texRgn) {
    float3 result = tap(texCoord, texRgn);
    if (result.x <= -99)
        return;
    accum += result;
    count += 1;
}

void NormalsFromLightmapsPixelShader(
    in float2 texCoord     : TEXCOORD0,
    in float4 texRgn       : TEXCOORD1,
    out float4 result      : COLOR0
) {
    if (tex2Dlod(LeftSampler, float4(texCoord, 0, 0)).a <= 0.01) {
        result = 0;
        discard;
    }

    /*
    float distance = tex2Dbias(TextureSampler, float4(clamp2(texCoord, texRgn.xy, texRgn.zw), 0, DistancePowersAndMipBias.z)).r;
    distance = max(params.x, distance);

    if (distance > params.y) {
        result = 0;
        discard;
        return;
    }

    distance -= params.x;
    distance /= (params.y - params.x);
    distance = 1 - pow(1 - saturate(pow(distance, DistancePowersAndMipBias.x)), DistancePowersAndMipBias.y);
    // Negative distance is higher so we want to go from max height to min height as distance increases
    distance = lerp(params.w, params.z, distance);
    */
    float3 texel = float3(BitmapTexelSize, 0);
    float tapCount = 1;
    float3 accum = tap(texCoord, texRgn), normal;
    if (BLUR) {
        conditionalTap(accum, tapCount, texCoord - texel.xy, texRgn);
        conditionalTap(accum, tapCount, texCoord - texel.zy, texRgn);
        conditionalTap(accum, tapCount, texCoord + float2(texel.x, -texel.y), texRgn);
        conditionalTap(accum, tapCount, texCoord - texel.xz, texRgn);
        conditionalTap(accum, tapCount, texCoord + texel.xz, texRgn);
        conditionalTap(accum, tapCount, texCoord + float2(-texel.x, texel.y), texRgn);
        conditionalTap(accum, tapCount, texCoord + texel.zy, texRgn);
        conditionalTap(accum, tapCount, texCoord + texel.xy, texRgn);
        normal = accum / tapCount;
    } else {
        normal = accum;
    }

    if (length(abs(normal)) > 99) {
        result = float4(0, 0, 0, 1);
    } else {
        result = float4((normal * 0.5) + 0.5, 1);
    }
}

technique NormalsFromLightmaps
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 NormalsFromLightmapsPixelShader();
    }
}