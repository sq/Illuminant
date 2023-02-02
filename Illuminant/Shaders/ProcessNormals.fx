#include "..\..\..\Fracture\Squared\RenderLib\Shaders\CompilerWorkarounds.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\TargetInfo.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\ViewTransformCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\FormatCommon.fxh"
#include "..\..\..\Fracture\Squared\RenderLib\Shaders\BitmapCommon.fxh"

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
    float left = cleanInput(tex2Dlod(LeftSampler, uv)),
        right = InputCount > 1 ? cleanInput(tex2Dlod(RightSampler, uv)) : 1.0 - left,
        above = InputCount > 2 ? cleanInput(tex2Dlod(AboveSampler, uv)) : 0.0,
        below = InputCount > 3 ? cleanInput(tex2Dlod(BelowSampler, uv)) : 1.0 - above,
        xDelta = right - left,
        yDelta = below - above,
        xyLength = length(float2(xDelta, yDelta)),
        forward = xyLength <= 0.01
            ? 1.0
            : (xyLength >= 0.98
                ? 0.0
                : sqrt(1.0 - xyLength)
                ) * Configuration1.z;
    float3 n = float3(xDelta, yDelta, forward + Configuration1.w);
    return normalize(n);
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
    float3 normal = tap(texCoord, texRgn);
    result = float4((normal * 0.5) + 0.5, 1);
}

technique NormalsFromLightmaps
{
    pass P0
    {
        vertexShader = compile vs_3_0 ScreenSpaceVertexShader();
        pixelShader = compile ps_3_0 NormalsFromLightmapsPixelShader();
    }
}